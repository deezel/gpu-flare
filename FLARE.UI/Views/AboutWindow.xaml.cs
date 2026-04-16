using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using FLARE.UI.Helpers;

namespace FLARE.UI.Views;

public partial class AboutWindow : Window
{
    private const int Strips = 48;
    private const float HalfW = 1.6f;
    private const float HalfH = 0.4f;
    private const float HalfD = HalfH; // square cross-section — all faces same height
    private const float WindPeriod = 12.0f;
    private const float WindMaxAngle = 2.5f;
    private const float WindWaveSpread = MathF.PI / 2f;
    private const float CreepAmplitude = 1.6f;
    private const float CreepPeriodRatio = 1.618f;
    private const float StaticYTilt = 0.0f; // torsion wave provides all depth cues

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Point3DCollection _positions;

    public AboutWindow()
    {
        InitializeComponent();
        TitleBarHelper.SetDarkTitleBar(this);

        var asm = Assembly.GetExecutingAssembly();
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        int plus = infoVer.IndexOf('+');
        var ver = plus >= 0 ? infoVer[..plus] : infoVer;
        var hash = plus >= 0 ? infoVer[(plus + 1)..] : "";
        var buildDate = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value ?? "";
        var verText = string.IsNullOrEmpty(hash) || hash == "dev" ? $"Version {ver}" : $"Version {ver} ({hash})";
        VersionText.Text = $"{verText}  ·  {buildDate}";
        RuntimeText.Text = $".NET {RuntimeInformation.FrameworkDescription.Replace(".NET ", "")} / {RuntimeInformation.RuntimeIdentifier}";

        var mesh = BuildMesh(out _positions);
        var logo = new BitmapImage(new Uri("pack://application:,,,/Logo/flare_filled_logo.png"));
        var logoBrush = new ImageBrush(logo) { Stretch = Stretch.Fill };

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(0x50, 0x50, 0x50)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0xCC, 0xCC, 0xCC), new Vector3D(-0.3, -0.2, -1)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0x44, 0x44, 0x44), new Vector3D(0.3, 0.2, 1)));
        group.Children.Add(new GeometryModel3D(mesh, new DiffuseMaterial(logoBrush))
        {
            BackMaterial = new DiffuseMaterial(logoBrush)
        });

        BoxModel.Content = group;

        CompositionTarget.Rendering += OnRender;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRender;
    }

    private MeshGeometry3D BuildMesh(out Point3DCollection positions)
    {
        // 8 vertices per strip edge:
        // 0=front-top, 1=front-bottom, 2=back-top, 3=back-bottom
        // 4=top-front, 5=top-back, 6=bottom-back, 7=bottom-front
        positions = new Point3DCollection((Strips + 1) * 8);
        var texCoords = new PointCollection((Strips + 1) * 8);
        var indices = new Int32Collection(Strips * 4 * 6);

        for (int i = 0; i <= Strips; i++)
        {
            float u = (float)i / Strips;
            for (int j = 0; j < 8; j++)
                positions.Add(new Point3D(0, 0, 0));

            // UVs corrected so logo reads left-to-right, right-side-up on every face
            // Padding so logo doesn't bleed to edges
            const float pad = 0.05f;
            float pu = pad + u * (1f - 2f * pad);         // padded U
            float rpu = pad + (1f - u) * (1f - 2f * pad); // padded U reversed
            float v0 = pad;
            float v1 = 1f - pad;
            texCoords.Add(new Point(pu, v0));  // 0 front-top
            texCoords.Add(new Point(pu, v1));  // 1 front-bottom
            texCoords.Add(new Point(pu, v1));  // 2 back-top (V flipped — card is upside down at 180°)
            texCoords.Add(new Point(pu, v0));  // 3 back-bottom
            texCoords.Add(new Point(pu, v1));  // 4 top-front
            texCoords.Add(new Point(pu, v0));  // 5 top-back
            texCoords.Add(new Point(pu, v1));  // 6 bottom-back
            texCoords.Add(new Point(pu, v0));  // 7 bottom-front
        }

        for (int i = 0; i < Strips; i++)
        {
            int l = i * 8;
            int r = (i + 1) * 8;
            // Front
            AddQuad(indices, l + 0, r + 0, r + 1, l + 1);
            // Back
            AddQuad(indices, r + 2, l + 2, l + 3, r + 3);
            // Top
            AddQuad(indices, l + 4, r + 4, r + 5, l + 5);
            // Bottom
            AddQuad(indices, r + 6, l + 6, l + 7, r + 7);
        }

        return new MeshGeometry3D
        {
            Positions = positions,
            TextureCoordinates = texCoords,
            TriangleIndices = indices
        };
    }

    private static void AddQuad(Int32Collection indices, int a, int b, int c, int d)
    {
        indices.Add(a); indices.Add(b); indices.Add(c);
        indices.Add(a); indices.Add(c); indices.Add(d);
    }

    private const float StartDelay = 2.5f;
    private const float RampDuration = 1.5f;

    private void OnRender(object? sender, EventArgs e)
    {
        float rawTime = (float)_clock.Elapsed.TotalSeconds;
        float animTime = MathF.Max(0f, rawTime - StartDelay);
        float ramp = MathF.Min(1f, animTime / RampDuration);
        ramp = ramp * ramp * (3f - 2f * ramp); // smoothstep ease-in
        float windAngle = 2f * MathF.PI * animTime / WindPeriod;
        float creepPhase = 2f * MathF.PI * animTime / (WindPeriod * CreepPeriodRatio);
        float cosY = MathF.Cos(StaticYTilt);
        float sinY = MathF.Sin(StaticYTilt);

        for (int i = 0; i <= Strips; i++)
        {
            float t = (float)i / Strips;
            float x = HalfW * (2f * t - 1f);
            float theta = ramp * (WindMaxAngle * MathF.Sin(windAngle - t * WindWaveSpread)
                                + CreepAmplitude * MathF.Sin(creepPhase - t * WindWaveSpread * 0.7f));

            float cosT = MathF.Cos(theta);
            float sinT = MathF.Sin(theta);

            Point3D Transform(float ly, float lz)
            {
                float yt = ly * cosT - lz * sinT;
                float zt = ly * sinT + lz * cosT;
                return new Point3D(x * cosY + zt * sinY, yt, -x * sinY + zt * cosY);
            }

            var ft = Transform(+HalfH, +HalfD);
            var fb = Transform(-HalfH, +HalfD);
            var bt = Transform(+HalfH, -HalfD);
            var bb = Transform(-HalfH, -HalfD);

            int b8 = i * 8;
            _positions[b8 + 0] = ft; // front-top
            _positions[b8 + 1] = fb; // front-bottom
            _positions[b8 + 2] = bt; // back-top
            _positions[b8 + 3] = bb; // back-bottom
            _positions[b8 + 4] = ft; // top-front
            _positions[b8 + 5] = bt; // top-back
            _positions[b8 + 6] = bb; // bottom-back
            _positions[b8 + 7] = fb; // bottom-front
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
