# GPU Error Analysis Report — Dump Details

Generated: 2026-05-16 10:43:27
Companion to: `flare_report_20260516_104327.md`

## LIVE KERNEL DUMP ANALYSIS

### WATCHDOG-20260515-1151.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffffad00`7eea6480 fffff800`e3b05ad9     : ffffd102`e46f5010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffffad00`7eea69b0 fffff800`e3ea487a     : 00000000`00000000 00000000`c0000022 fffff800`e3ee93c0 00000000`c0000022 : nt!DbgkpWerProcessPolicyResult+0x21
      ffffad00`7eea69e0 fffff800`e3ea4679     : 00000000`00000003 ffffad00`7eea6bc0 ffffd102`e46f5010 ffffd102`ee6489d0 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffffad00`7eea6a40 fffff800`75572ef9     : ffffc10e`301ebb10 ffffc10e`301ebb10 ffffc10e`3317528c ffffd102`e46f5010 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffffad00`7eea6ac0 fffff800`c75d53d2     : 00000000`00000006 00000000`00000006 ffffd102`d24b3000 ffffd102`d24ba650 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffffad00`7eea6c80 fffff800`c7635b16     : ffffd102`d24b3000 ffffd102`d24b3000 ffffd102`d242d000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffffad00`7eea6e70 fffff800`c764624f     : 00000000`00000000 00000000`00000000 ffffd102`d24b3000 ffffad00`7eea6f80 : dxgmms2!VidSchiResetEngines+0xea
      ffffad00`7eea6ec0 fffff800`c769498e     : ffffd102`d24b3000 ffffffff`feced300 ffffd102`d242d000 00000000`00000001 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffffad00`7eea6f40 fffff800`c75c94af     : 00000000`00000000 ffffd102`d242d000 ffffd102`d24b3000 fffff800`75215519 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffffad00`7eea7030 fffff800`c769fceb     : 00000000`00000000 00000000`00000014 00000000`00000000 ffffd102`d242d000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260515-1136.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffffdd80`e90665f0 fffff805`76b05ad9     : ffff968f`a48923f0 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffffdd80`e9066b20 fffff805`76ea487a     : 00000000`00000000 00000000`c0000022 fffff805`76ee93c0 00000000`c0000022 : nt!DbgkpWerProcessPolicyResult+0x21
      ffffdd80`e9066b50 fffff805`76ea4679     : 00000000`00000003 ffffdd80`e9066d30 ffff968f`a48923f0 ffff968f`a21c99d0 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffffdd80`e9066bb0 fffff805`08762ef9     : ffffcd85`bf5a5230 ffffcd85`bf5a5230 ffffcd85`c116e346 ffff968f`a48923f0 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffffdd80`e9066c30 fffff805`59e853d2     : 00000000`00000006 00000000`00000006 ffff968f`77569000 ffff968f`7756f1c0 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffffdd80`e9066df0 fffff805`59ee5b16     : ffff968f`77569000 ffff968f`77569000 ffff968f`77431000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffffdd80`e9066fe0 fffff805`59ef624f     : 00000000`00000000 00000000`00000000 ffff968f`77569000 ffffdd80`e90670f0 : dxgmms2!VidSchiResetEngines+0xea
      ffffdd80`e9067030 fffff805`59f4498e     : ffff968f`77569000 ffffffff`feced300 ffff968f`77431000 00000000`00002000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffffdd80`e90670b0 fffff805`59e794af     : 00000000`0000000d ffff968f`77431000 ffff968f`77569000 fffff805`59e55b28 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffffdd80`e90671a0 fffff805`59f4fceb     : 00000000`0000000d 00000000`00000014 00000000`0000000d ffff968f`77431000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260515-1027.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff958b`a25cc5f0 fffff802`e0905ad9     : ffff9b04`efec5050 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffff958b`a25ccb20 fffff802`e0ca487a     : 00000000`00000000 00000000`c0000022 fffff802`e0ce93c0 00000000`c0000022 : nt!DbgkpWerProcessPolicyResult+0x21
      ffff958b`a25ccb50 fffff802`e0ca4679     : 00000000`00000003 ffff958b`a25ccd30 ffff9b04`efec5050 ffff9b04`e782be00 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffff958b`a25ccbb0 fffff802`72402ef9     : ffffab84`6bfe27b0 ffffab84`6bfe27b0 ffffab84`6d241ae0 ffff9b04`e3fbe000 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffff958b`a25ccc30 fffff802`770053d2     : 00000000`00000006 00000000`00000006 ffff9b04`e4add000 ffff9b04`e4ae48f0 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffff958b`a25ccdf0 fffff802`77065b16     : ffff9b04`e4add000 ffff9b04`e4add000 ffff9b04`e3fc0000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffff958b`a25ccfe0 fffff802`7707624f     : 00000000`00000000 00000000`00000000 ffff9b04`e4add000 ffff958b`a25cd0f0 : dxgmms2!VidSchiResetEngines+0xea
      ffff958b`a25cd030 fffff802`770c498e     : ffff9b04`e4add000 ffffffff`feced300 ffff9b04`e3fc0000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffff958b`a25cd0b0 fffff802`76ff94af     : 00000000`0000000c ffff9b04`e3fc0000 ffff9b04`e4add000 ffff958b`a25cd308 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffff958b`a25cd1a0 fffff802`770cfceb     : 00000000`0000000c 00000000`00000014 00000000`0000000c ffff9b04`e3fc0000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260515-0932.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffffce0f`8d4565f0 fffff800`81d07ad9     : ffff840d`43151010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffffce0f`8d456b20 fffff800`820a4b2a     : 00000000`00000000 00000000`00000000 fffff800`820e9340 00000000`c0000022 : nt!DbgkpWerProcessPolicyResult+0x21
      ffffce0f`8d456b50 fffff800`820a4929     : 00000000`00000003 ffffce0f`8d456d30 ffff840d`43151010 ffff840d`4d294540 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffffce0f`8d456bb0 fffff800`13c09349     : ffffd089`e99ab790 ffffd089`e99ab790 ffffd08a`0324942d ffff840d`43151010 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffffce0f`8d456c30 fffff800`64e47616     : 00000000`00000006 00000000`00000006 ffff840d`1f5c6000 ffff840d`1f5bf930 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffffce0f`8d456df0 fffff800`64ea6a56     : ffff840d`1f5c6000 ffff840d`1f5c6000 ffff840d`1f4a6000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffffce0f`8d456fe0 fffff800`64eb71af     : 00000000`00000000 00000000`00000000 ffff840d`1f5c6000 ffffce0f`8d4570f0 : dxgmms2!VidSchiResetEngines+0xea
      ffffce0f`8d457030 fffff800`64f058ee     : ffff840d`1f5c6000 ffffffff`feced300 ffff840d`1f4a6000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffffce0f`8d4570b0 fffff800`64e3b41f     : 00000000`0000000c ffff840d`1f4a6000 ffff840d`1f5c6000 fffff800`64e17178 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffffce0f`8d4571a0 fffff800`64f10c4b     : 00000000`0000000c 00000000`00000014 00000000`0000000c ffff840d`1f4a6000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260515-0059.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      fffff287`564963b0 fffff801`ef307ad9     : ffffd90b`90868050 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      fffff287`564968e0 fffff801`ef6a4b2a     : 00000000`00000000 00000000`00000000 fffff801`ef6e9340 00000000`c0000022 : nt!DbgkpWerProcessPolicyResult+0x21
      fffff287`56496910 fffff801`ef6a4929     : 00000000`00000003 fffff287`56496af0 ffffd90b`90868050 ffffd90b`9148fd10 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      fffff287`56496970 fffff801`81229349     : ffffc782`42beece0 ffffc782`42beece0 ffffc782`6f241c50 ffffd90b`90868050 : nt!DbgkWerCaptureLiveKernelDump+0x69
      fffff287`564969f0 fffff801`d2497616     : 00000000`00000006 00000000`00000006 ffffd90b`6248e000 ffffd90b`62494150 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      fffff287`56496bb0 fffff801`d24f6a56     : ffffd90b`6248e000 ffffd90b`6248e000 ffffd90b`623bb000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      fffff287`56496da0 fffff801`d25071af     : 00000000`00000000 00000000`00000000 ffffd90b`6248e000 fffff287`56496eb0 : dxgmms2!VidSchiResetEngines+0xea
      fffff287`56496df0 fffff801`d25558ee     : ffffd90b`6248e000 ffffffff`feced300 ffffd90b`623bb000 00000000`00000001 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      fffff287`56496e70 fffff801`d248b41f     : 00000000`00000000 ffffd90b`623bb000 ffffd90b`6248e000 fffff801`80ed5519 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      fffff287`56496f60 fffff801`d2560c4b     : 00000000`00000000 00000000`00000014 00000000`00000000 ffffd90b`623bb000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260514-2217.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff9c89`2deee5f0 fffff801`73d07ad9     : ffff8880`1f98d010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffff9c89`2deeeb20 fffff801`740a4b2a     : 00000000`00000000 00000000`00000000 fffff801`740e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      ffff9c89`2deeeb50 fffff801`740a4929     : 00000000`00000003 ffff9c89`2deeed30 ffff8880`1f98d010 ffff8880`19b3e280 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffff9c89`2deeebb0 fffff801`05d59349     : ffffaf03`ac9bbf10 ffffaf03`ac9bbf10 ffffaf03`e0271c09 ffff8880`1f98d010 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffff9c89`2deeec30 fffff801`50a47616     : 00000000`00000006 00000000`00000006 ffff8880`12af6000 ffff8880`12afd650 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffff9c89`2deeedf0 fffff801`50aa6a56     : ffff8880`12af6000 ffff8880`12af6000 ffff8880`11ff1000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffff9c89`2deeefe0 fffff801`50ab71af     : 00000000`00000000 00000000`00000000 ffff8880`12af6000 ffff9c89`2deef0f0 : dxgmms2!VidSchiResetEngines+0xea
      ffff9c89`2deef030 fffff801`50b058ee     : ffff8880`12af6000 ffffffff`feced300 ffff8880`11ff1000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffff9c89`2deef0b0 fffff801`50a3b41f     : 00000000`0000000c ffff8880`11ff1000 ffff8880`12af6000 fffff801`50a17178 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffff9c89`2deef1a0 fffff801`50b10c4b     : 00000000`0000000c 00000000`00000014 00000000`0000000c ffff8880`11ff1000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260514-2144.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff8188`3bcae8f0 fffff803`e3f07ad9     : ffff9881`3cf4d010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffff8188`3bcaee20 fffff803`e42a4b2a     : 00000000`00000000 00000000`00000000 fffff803`e42e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      ffff8188`3bcaee50 fffff803`e42a4929     : 00000000`00000003 ffff8188`3bcaf030 ffff9881`3cf4d010 ffff9881`33dbeca0 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffff8188`3bcaeeb0 fffff803`75ee9349     : ffffd684`613aba50 ffffd684`613aba50 ffffd684`7b37d2aa ffff9881`1d65e000 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffff8188`3bcaef30 fffff803`7a147616     : 00000000`00000006 00000000`00000006 ffff9881`1ce1a000 ffff9881`223ad730 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffff8188`3bcaf0f0 fffff803`7a1a6a56     : ffff9881`1ce1a000 ffff9881`1ce1a000 ffff9881`1c92a000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffff8188`3bcaf2e0 fffff803`7a1b77e6     : ffff9881`00000000 ffff9881`1c92a000 ffff9881`1c92a001 00000000`00000000 : dxgmms2!VidSchiResetEngines+0xea
      ffff8188`3bcaf330 fffff803`7a148d44     : fffff7b6`00000000 00000000`00000000 ffff9881`00000000 00000000`0007dc04 : dxgmms2!VidSchiCheckHwProgress+0x316
      ffff8188`3bcaf3c0 fffff803`7a1b9a16     : ffff9881`1ce1ed00 ffff9881`1c92a000 ffff9881`1ce1a000 ffff9881`1c92a000 : dxgmms2!VidSchWaitForEvents+0xb8
      ffff8188`3bcaf430 fffff803`7a121e7e     : ffff8188`3bcaf460 ffff9881`1e7c5ba0 ffff9881`1e042828 ffffc001`3fd58180 : dxgmms2!VidSchiSwitchNodeFromContext+0x126
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260514-2143.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff8188`3bcae5f0 fffff803`e3f07ad9     : ffff9881`3c91c010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffff8188`3bcaeb20 fffff803`e42a4b2a     : 00000000`00000000 00000000`00000000 fffff803`e42e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      ffff8188`3bcaeb50 fffff803`e42a4929     : 00000000`00000003 ffff8188`3bcaed30 ffff9881`3c91c010 ffff9881`3e1c33a0 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffff8188`3bcaebb0 fffff803`75ee9349     : ffffd684`3ae43120 ffffd684`3ae43120 ffffd684`76366784 ffff9881`1d65e000 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffff8188`3bcaec30 fffff803`7a147616     : 00000000`00000006 00000000`00000006 ffff9881`229da000 ffff9881`229e05b0 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffff8188`3bcaedf0 fffff803`7a1a6a56     : ffff9881`229da000 ffff9881`229da000 ffff9881`1c92a000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffff8188`3bcaefe0 fffff803`7a1b71af     : 00000000`00000000 00000000`00000000 ffff9881`229da000 ffff8188`3bcaf0f0 : dxgmms2!VidSchiResetEngines+0xea
      ffff8188`3bcaf030 fffff803`7a2058ee     : ffff9881`229da000 ffffffff`feced300 ffff9881`1c92a000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffff8188`3bcaf0b0 fffff803`7a13b41f     : 00000000`0000000c ffff9881`1c92a000 ffff9881`229da000 fffff803`7a117178 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffff8188`3bcaf1a0 fffff803`7a210c4b     : 00000000`0000000c 00000000`00000014 00000000`0000000c ffff9881`1c92a000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260514-1633.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff9905`8f8665f0 fffff802`d8107ad9     : ffffe583`545c8010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffff9905`8f866b20 fffff802`d84a4b2a     : 00000000`00000000 00000000`00000000 fffff802`d84e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      ffff9905`8f866b50 fffff802`d84a4929     : 00000000`00000003 ffff9905`8f866d30 ffffe583`545c8010 ffffe583`55675ac0 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffff9905`8f866bb0 fffff802`69f79349     : ffffae8b`a5ada760 ffffae8b`a5ada760 ffffae8b`e73e63f9 ffffe583`0d065000 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffff9905`8f866c30 fffff802`bcc47616     : 00000000`00000006 00000000`00000006 ffffe583`0d1c0000 ffffe583`0d1c71f0 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffff9905`8f866df0 fffff802`bcca6a56     : ffffe583`0d1c0000 ffffe583`0d1c0000 ffffe583`0d092000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffff9905`8f866fe0 fffff802`bccb71af     : ffff9905`00000000 00000000`00000000 ffffe583`0d1c0000 ffff9905`8f8670f0 : dxgmms2!VidSchiResetEngines+0xea
      ffff9905`8f867030 fffff802`bcd058ee     : ffffe583`0d1c0000 ffffffff`feced300 ffffe583`0d092000 00000000`00008000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffff9905`8f8670b0 fffff802`bcc3b41f     : 00000000`0000000f ffffe583`0d092000 ffffe583`0d1d7000 fffff802`bcc17178 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffff9905`8f8671a0 fffff802`bcd10c4b     : 00000000`00000011 00000000`00000017 00000000`00000011 ffffe583`0d092000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260514-1142.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffffe886`e7b62230 fffff803`57bb7128     : ffffd883`a84556c0 00000000`00000000 00000000`00000000 00000000`00000815 : watchdog!WdpDbgCaptureTriageDump+0xe6
      ffffe886`e7b622a0 fffff803`57bb3b81     : ffffd883`ce8d0b00 00000000`00000193 00000000`00000815 ffffd883`978e7180 : watchdog!WdDbgReportRecreate+0x108
      ffffe886`e7b62300 fffff803`57ba761e     : ffffd883`ce8d0bc0 ffffe886`e7b624c0 ffffd883`97668ae0 fffff803`57ba75d0 : watchdog!WdDbgReportCreate+0x91
      ffffe886`e7b62370 fffff803`c5a0817b     : ffffd883`978e7040 ffffd883`97668a00 ffffd883`97668a00 ffffd883`97668ae0 : watchdog!WdpProcessWatchdogTimeoutReportThread+0x4e
      ffffe886`e7b623c0 fffff803`c5c8782a     : ffffd883`978e7040 ffffd883`978e7040 fffff803`c5a07cc0 ffffd883`97668ae0 : nt!ExpWorkerThread+0x4bb
      ffffe886`e7b62570 fffff803`c5eab424     : ffff8081`17664180 ffffd883`978e7040 fffff803`c5c877d0 00000000`00000000 : nt!PspSystemThreadStartup+0x5a
      ffffe886`e7b625c0 00000000`00000000     : ffffe886`e7b63000 ffffe886`e7b5c000 00000000`00000000 00000000`00000000 : nt!KiStartSystemThread+0x34
    MODULE_NAME: watchdog
    IMAGE_NAME:  watchdog.sys
    FAILURE_BUCKET_ID:  LKD_0x193_watchdog!WdpDbgCaptureTriageDump
```

### WATCHDOG-20260514-1138.dmp

```text
    PROCESS_NAME:  csrss.exe
    STACK_TEXT (top frames):
      ffffe886`e9c3ec70 fffff803`57bb7128     : ffffd883`b50c7d00 00000000`00000000 00000000`00000000 00000000`00000002 : watchdog!WdpDbgCaptureTriageDump+0xe6
      ffffe886`e9c3ece0 fffff803`57bb3b48     : ffff9107`4a170700 00000000`000001b0 00000000`00000002 ffffe886`e9c3ee10 : watchdog!WdDbgReportRecreate+0x108
      ffffe886`e9c3ed40 fffff803`57d7a034     : ffff9107`4a170710 ffffe886`e9c3ee20 ffffd883`9d996360 ffffffff`c000009a : watchdog!WdDbgReportCreate+0x58
      ffffe886`e9c3edb0 fffff803`57e160fd     : 00000000`00000108 ffffd883`a9a23180 ffff9107`4a170710 ffffd883`a9944230 : dxgkrnl!DxgCreateLiveDumpWithDriverBlob+0x154
      ffffe886`e9c3ee40 fffff803`57e16ea1     : ffffd883`a9164800 00000000`00000000 ffffd883`a9a23180 00000000`00000000 : dxgkrnl!DpiFdoStartAdapter+0x1c85
      ffffe886`e9c3f040 fffff803`57e16b28     : ffffd883`a9a23180 ffffe886`e9c3f190 00000000`00000000 ffffd883`a9944230 : dxgkrnl!DpiFdoStartNonLdaAdapter+0x85
      ffffe886`e9c3f090 fffff803`57e16443     : fffff803`aadbadf0 00000000`00000001 00000000`00000000 00000000`00000000 : dxgkrnl!DpiFdoStartAdapterThreadImpl+0x60c
      ffffe886`e9c3f200 fffff803`57f98fca     : fffff803`57d2fd00 00000000`00000000 00000000`00000000 00000000`00000000 : dxgkrnl!DpiFdoStartAdapterThread+0x33
      ffffe886`e9c3f230 fffff803`57f98bca     : ffffffff`fffffffd 00000000`00000000 00000000`00000000 fffff803`57bb8076 : dxgkrnl!DpiSessionCreateCallback+0x7e
      ffffe886`e9c3f290 fffff803`57bb442a     : 00000000`00000000 00000000`00000100 00000000`00000000 00000000`00000100 : dxgkrnl!DxgkNotifySessionStateChange+0x5a
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: dxgkrnl
    IMAGE_NAME:  dxgkrnl.sys
    FAILURE_BUCKET_ID:  LKD_0x1B0_dxgkrnl!DxgCreateLiveDumpWithDriverBlob
```

### WATCHDOG-20260514-1118.dmp

```text
    PROCESS_NAME:  csrss.exe
    STACK_TEXT (top frames):
      fffff984`f8c2ec70 fffff802`7bb37128     : ffffd507`0166acf0 00000000`00000000 00000000`00000000 00000000`00000002 : watchdog!WdpDbgCaptureTriageDump+0xe6
      fffff984`f8c2ece0 fffff802`7bb33b48     : ffff8402`61179300 00000000`000001b0 00000000`00000002 fffff984`f8c2ee10 : watchdog!WdDbgReportRecreate+0x108
      fffff984`f8c2ed40 fffff802`7bcfa034     : ffff8402`61179310 fffff984`f8c2ee20 ffffd506`e9aad360 ffffffff`c000009a : watchdog!WdDbgReportCreate+0x58
      fffff984`f8c2edb0 fffff802`7bd960fd     : 00000000`00000108 ffffd506`f59d4180 ffff8402`61179310 ffffd506`f5a30900 : dxgkrnl!DxgCreateLiveDumpWithDriverBlob+0x154
      fffff984`f8c2ee40 fffff802`7bd96ea1     : ffffd506`e98cc800 00000000`00000000 ffffd506`f59d4180 00000000`00000000 : dxgkrnl!DpiFdoStartAdapter+0x1c85
      fffff984`f8c2f040 fffff802`7bd96b28     : ffffd506`f59d4180 fffff984`f8c2f190 00000000`00000000 ffffd506`f5a30900 : dxgkrnl!DpiFdoStartNonLdaAdapter+0x85
      fffff984`f8c2f090 fffff802`7bd96443     : fffff802`801cadf0 00000000`00000001 00000000`00000000 00000000`00000000 : dxgkrnl!DpiFdoStartAdapterThreadImpl+0x60c
      fffff984`f8c2f200 fffff802`7bf18fca     : fffff802`7bcafd00 00000000`00000000 00000000`00000000 00000000`00000000 : dxgkrnl!DpiFdoStartAdapterThread+0x33
      fffff984`f8c2f230 fffff802`7bf18bca     : ffffffff`fffffffd 00000000`00000000 00000000`00000000 fffff802`7bb38076 : dxgkrnl!DpiSessionCreateCallback+0x7e
      fffff984`f8c2f290 fffff802`7bb3442a     : 00000000`00000000 00000000`00000108 00000000`00000000 00000000`00000108 : dxgkrnl!DxgkNotifySessionStateChange+0x5a
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: dxgkrnl
    IMAGE_NAME:  dxgkrnl.sys
    FAILURE_BUCKET_ID:  LKD_0x1B0_dxgkrnl!DxgCreateLiveDumpWithDriverBlob
```

### WATCHDOG-20260514-1114.dmp

```text
    PROCESS_NAME:  csrss.exe
    STACK_TEXT (top frames):
      ffffc307`f8246c70 fffff804`63187128     : ffff990e`f3282560 00000000`00000000 00000000`00000000 00000000`00000002 : watchdog!WdpDbgCaptureTriageDump+0xe6
      ffffc307`f8246ce0 fffff804`63183b48     : ffff840e`9e704700 00000000`000001b0 00000000`00000002 ffffc307`f8246e10 : watchdog!WdDbgReportRecreate+0x108
      ffffc307`f8246d40 fffff804`6334a034     : ffff840e`9e704710 ffffc307`f8246e20 ffff990e`d6985360 ffffffff`c000009a : watchdog!WdDbgReportCreate+0x58
      ffffc307`f8246db0 fffff804`633e60fd     : 00000000`00000108 ffff990e`e2e9e180 ffff840e`9e704710 ffff990e`e29e5070 : dxgkrnl!DxgCreateLiveDumpWithDriverBlob+0x154
      ffffc307`f8246e40 fffff804`633e6ea1     : ffff990e`d68d4800 00000000`00000000 ffff990e`e2e9e180 00000000`00000000 : dxgkrnl!DpiFdoStartAdapter+0x1c85
      ffffc307`f8247040 fffff804`633e6b28     : ffff990e`e2e9e180 ffffc307`f8247190 00000000`00000000 ffff990e`e29e5070 : dxgkrnl!DpiFdoStartNonLdaAdapter+0x85
      ffffc307`f8247090 fffff804`633e6443     : fffff804`a6bbadf0 00000000`00000001 00000000`00000000 00000000`00000000 : dxgkrnl!DpiFdoStartAdapterThreadImpl+0x60c
      ffffc307`f8247200 fffff804`63568fca     : fffff804`632ffd00 00000000`00000000 00000000`00000000 00000000`00000000 : dxgkrnl!DpiFdoStartAdapterThread+0x33
      ffffc307`f8247230 fffff804`63568bca     : ffffffff`fffffffd 00000000`00000000 00000000`00000000 fffff804`63188076 : dxgkrnl!DpiSessionCreateCallback+0x7e
      ffffc307`f8247290 fffff804`6318442a     : 00000000`00000000 00000000`00000100 00000000`00000000 00000000`00000100 : dxgkrnl!DxgkNotifySessionStateChange+0x5a
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: dxgkrnl
    IMAGE_NAME:  dxgkrnl.sys
    FAILURE_BUCKET_ID:  LKD_0x1B0_dxgkrnl!DxgCreateLiveDumpWithDriverBlob
```

### WATCHDOG-20260514-1043.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      fffff884`a09865f0 fffff807`82b07ad9     : ffff918a`e79df010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      fffff884`a0986b20 fffff807`82ea4b2a     : 00000000`00000000 00000000`00000000 fffff807`82ee9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      fffff884`a0986b50 fffff807`82ea4929     : 00000000`00000003 fffff884`a0986d30 ffff918a`e79df010 ffff918a`e1237980 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      fffff884`a0986bb0 fffff807`14a79349     : ffffa50c`94d4f5f0 ffffa50c`94d4f5f0 ffffa50c`9937c38e ffff918a`bed5e000 : nt!DbgkWerCaptureLiveKernelDump+0x69
      fffff884`a0986c30 fffff807`57ed7616     : 00000000`00000006 00000000`00000006 ffff918a`beea0000 ffff918a`bee99d90 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      fffff884`a0986df0 fffff807`57f36a56     : ffff918a`beea0000 ffff918a`beea0000 ffff918a`bed61000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      fffff884`a0986fe0 fffff807`57f471af     : 00000000`00000000 00000000`00000000 ffff918a`beea0000 fffff884`a09870f0 : dxgmms2!VidSchiResetEngines+0xea
      fffff884`a0987030 fffff807`57f958ee     : ffff918a`beea0000 ffffffff`feced300 ffff918a`bed61000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      fffff884`a09870b0 fffff807`57ecb41f     : 00000000`0000000c ffff918a`bed61000 ffff918a`beea0000 fffff884`a0987308 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      fffff884`a09871a0 fffff807`57fa0c4b     : 00000000`0000000c 00000000`00000014 00000000`0000000c ffff918a`bed61000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260514-0143.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff958c`1f8be5f0 fffff802`a3f07ad9     : ffff988c`b5ae0050 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffff958c`1f8beb20 fffff802`a42a4b2a     : 00000000`00000000 00000000`00000000 fffff802`a42e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      ffff958c`1f8beb50 fffff802`a42a4929     : 00000000`00000003 ffff958c`1f8bed30 ffff988c`b5ae0050 ffff988c`9fb17f40 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffff958c`1f8bebb0 fffff802`35e19349     : ffffbf87`dea22300 ffffbf87`dea22300 ffffbf88`0535ffb5 ffff988c`b5ae0050 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffff958c`1f8bec30 fffff802`86e47616     : 00000000`00000006 00000000`00000006 ffff988c`78f8f000 ffff988c`78f96ab0 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffff958c`1f8bedf0 fffff802`86ea6a56     : ffff988c`78f8f000 ffff988c`78f8f000 ffff988c`78e54000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffff958c`1f8befe0 fffff802`86eb71af     : 00000000`00000000 00000000`00000000 ffff988c`78f8f000 ffff958c`1f8bf0f0 : dxgmms2!VidSchiResetEngines+0xea
      ffff958c`1f8bf030 fffff802`86f058ee     : ffff988c`78f8f000 ffffffff`feced300 ffff988c`78e54000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffff958c`1f8bf0b0 fffff802`86e3b41f     : 00000000`0000000c ffff988c`78e54000 ffff988c`78f8f000 fffff802`86e17178 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffff958c`1f8bf1a0 fffff802`86f10c4b     : 00000000`0000000c 00000000`00000014 00000000`0000000c ffff988c`78e54000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260513-2144.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      fffff600`198ce5f0 fffff807`ab507ad9     : ffffbb84`4aaaa010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      fffff600`198ceb20 fffff807`ab8a4b2a     : 00000000`00000000 00000000`00000000 fffff807`ab8e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      fffff600`198ceb50 fffff807`ab8a4929     : 00000000`00000003 fffff600`198ced30 ffffbb84`4aaaa010 ffffbb84`40d100b0 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      fffff600`198cebb0 fffff807`3d459349     : ffff9407`03f03e00 ffff9407`03f03e00 ffff9407`3c2758df ffffbb84`0ddd7000 : nt!DbgkWerCaptureLiveKernelDump+0x69
      fffff600`198cec30 fffff807`8ec97616     : 00000000`00000006 00000000`00000006 ffffbb84`0df18000 ffffbb84`0df1fab0 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      fffff600`198cedf0 fffff807`8ecf6a56     : ffffbb84`0df18000 ffffbb84`0df18000 ffffbb84`0ddda000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      fffff600`198cefe0 fffff807`8ed071af     : 00000000`00000000 00000000`00000000 ffffbb84`0df18000 fffff600`198cf0f0 : dxgmms2!VidSchiResetEngines+0xea
      fffff600`198cf030 fffff807`8ed558ee     : ffffbb84`0df18000 ffffffff`feced300 ffffbb84`0ddda000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      fffff600`198cf0b0 fffff807`8ec8b41f     : 00000000`0000000c ffffbb84`0ddda000 ffffbb84`0df18000 fffff807`8ec67178 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      fffff600`198cf1a0 fffff807`8ed60c4b     : 00000000`0000000c 00000000`00000014 00000000`0000000c ffffbb84`0ddda000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260513-1758.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff818b`3c3645f0 fffff807`a1f07ad9     : ffffc307`e39e8010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffff818b`3c364b20 fffff807`a22a4b2a     : 00000000`00000000 00000000`00000000 fffff807`a22e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      ffff818b`3c364b50 fffff807`a22a4929     : 00000000`00000003 ffff818b`3c364d30 ffffc307`e39e8010 ffffc307`f167cc10 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffff818b`3c364bb0 fffff807`33e99349     : ffffd30f`aaac7ec0 ffffd30f`aaac7ec0 ffffd30f`af279679 ffffc307`e39e8010 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffff818b`3c364c30 fffff807`7ec97616     : 00000000`00000006 00000000`00000006 ffffc307`abf6a000 ffffc307`abf718f0 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffff818b`3c364df0 fffff807`7ecf6a56     : ffffc307`abf6a000 ffffc307`abf6a000 ffffc307`967a7000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffff818b`3c364fe0 fffff807`7ed071af     : 00000000`00000000 00000000`00000000 ffffc307`abf6a000 ffff818b`3c3650f0 : dxgmms2!VidSchiResetEngines+0xea
      ffff818b`3c365030 fffff807`7ed558ee     : ffffc307`abf6a000 ffffffff`feced300 ffffc307`967a7000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffff818b`3c3650b0 fffff807`7ec8b41f     : 00000000`0000000c ffffc307`967a7000 ffffc307`abf6a000 fffff807`7ec67178 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffff818b`3c3651a0 fffff807`7ed60c4b     : 00000000`0000000c 00000000`00000014 00000000`0000000c ffffc307`967a7000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260512-2148.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffffe003`324765f0 fffff806`b6707ad9     : ffffae8b`3760e010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffffe003`32476b20 fffff806`b6aa4b2a     : 00000000`00000000 00000000`00000000 fffff806`b6ae9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      ffffe003`32476b50 fffff806`b6aa4929     : 00000000`00000003 ffffe003`32476d30 ffffae8b`3760e010 ffffae8b`3f8ae2c0 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffffe003`32476bb0 fffff806`48629349     : ffffe70c`e1d8a8a0 ffffe70c`e1d8a8a0 ffffe70c`ef35e905 ffffae8b`13e94000 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffffe003`32476c30 fffff806`92e47616     : 00000000`00000006 00000000`00000006 ffffae8b`13fa4000 ffffae8b`13faa690 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffffe003`32476df0 fffff806`92ea6a56     : ffffae8b`13fa4000 ffffae8b`13fa4000 ffffae8b`13e96000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffffe003`32476fe0 fffff806`92eb71af     : 00000000`00000000 00000000`00000000 ffffae8b`13fa4000 ffffe003`324770f0 : dxgmms2!VidSchiResetEngines+0xea
      ffffe003`32477030 fffff806`92f058ee     : ffffae8b`13fa4000 ffffffff`feced300 ffffae8b`13e96000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffffe003`324770b0 fffff806`92e3b41f     : 00000000`0000000c ffffae8b`13e96000 ffffae8b`13fa4000 ffffe003`32477308 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffffe003`324771a0 fffff806`92f10c4b     : 00000000`0000000c 00000000`00000014 00000000`0000000c ffffae8b`13e96000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260512-2040.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff8789`410c6600 fffff803`53fb7128     : ffff9501`2f6b2840 00000000`00000000 00000000`00000000 00000000`00000001 : watchdog!WdpDbgCaptureTriageDump+0xe6
      ffff8789`410c6670 fffff803`53fb3b48     : ffffa58f`78e77100 00000000`000001b0 00000000`00000001 ffff8789`410c67a0 : watchdog!WdDbgReportRecreate+0x108
      ffff8789`410c66d0 fffff803`5417a034     : ffffa58f`78e77110 ffff8789`410c67b0 ffff950f`fb9e7360 ffffffff`c0000010 : watchdog!WdDbgReportCreate+0x58
      ffff8789`410c6740 fffff803`5420a399     : 00000000`00080000 ffffffff`c0000010 ffffa58f`78e77110 00000000`00000000 : dxgkrnl!DxgCreateLiveDumpWithDriverBlob+0x154
      ffff8789`410c67d0 fffff803`9a2d2d89     : ffff950f`fb9e7360 ffff950f`fbaa8df0 ffff950f`fb9e7360 ffff950f`fbaa8df0 : dxgkrnl!DpiAddDevice+0x21f9
      ffff8789`410c6d90 ffff950f`fb9e7360     : ffff950f`fbaa8df0 ffff950f`fb9e7360 ffff950f`fbaa8df0 ffff8789`410c6dd4 : nvlddmkm+0x1a22d89
      ffff8789`410c6d98 ffff950f`fbaa8df0     : ffff950f`fb9e7360 ffff950f`fbaa8df0 ffff8789`410c6dd4 00000000`00000000 : 0xffff950f`fb9e7360
      ffff8789`410c6da0 ffff950f`fb9e7360     : ffff950f`fbaa8df0 ffff8789`410c6dd4 00000000`00000000 ffff8789`410c6e00 : 0xffff950f`fbaa8df0
      ffff8789`410c6da8 ffff950f`fbaa8df0     : ffff8789`410c6dd4 00000000`00000000 ffff8789`410c6e00 00000000`00000001 : 0xffff950f`fb9e7360
      ffff8789`410c6db0 ffff8789`410c6dd4     : 00000000`00000000 ffff8789`410c6e00 00000000`00000001 00000000`00000001 : 0xffff950f`fbaa8df0
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: dxgkrnl
    IMAGE_NAME:  dxgkrnl.sys
    FAILURE_BUCKET_ID:  LKD_0x1B0_dxgkrnl!DxgCreateLiveDumpWithDriverBlob
```

### WATCHDOG4401-20260512-2038.dmp

```text
    PROCESS_NAME:  dwm.exe
    STACK_TEXT (top frames):
      ffff8789`4ead16a0 fffff803`53fb7128     : ffff9501`31ebdaa0 ffff9501`1507a030 ffff9501`1507a030 00000000`00000001 : watchdog!WdpDbgCaptureTriageDump+0xe6
      ffff8789`4ead1710 fffff803`53fb3b48     : ffffa58f`a3e0fa00 00000000`000001b8 00000000`00000001 ffff8789`4ead1840 : watchdog!WdDbgReportRecreate+0x108
      ffff8789`4ead1770 fffff803`5417a034     : ffffa58f`a3e0fa98 ffff8789`4ead1850 ffff9501`1507a030 00000000`00000000 : watchdog!WdDbgReportCreate+0x58
      ffff8789`4ead17e0 fffff803`5418f4f6     : ffffa58f`a3e0ede0 ffff9501`2181c000 00000000`00000000 fffff803`53fae9c8 : dxgkrnl!DxgCreateLiveDumpWithDriverBlob+0x154
      ffff8789`4ead1870 fffff803`5419022c     : ffffa58f`a3e0e000 ffffa58f`a3e0e000 00000000`00000001 00000000`000016f9 : dxgkrnl!DISPLAYDIAGNOSTICADAPTERDATA::CreateMiniportBlackboxLiveDump+0xca
      ffff8789`4ead18f0 fffff803`54190e28     : ffffa58f`00000000 ffffa58f`a3e0e000 00000000`00000000 fffff803`53fae9c8 : dxgkrnl!DISPLAYSTATECHECKER::LogAllDisplayDiagInfo+0xec
      ffff8789`4ead1950 fffff803`580e9adc     : ffff9501`114ef020 ffffa58f`84de2ed0 ffffa58f`84de2ed4 fffff803`c277118d : dxgkrnl!DxgkCheckDisplayState+0x118
      ffff8789`4ead19d0 fffff803`580caa55     : ffff9501`114ef020 fffff803`53ee6090 00000000`00000032 00000000`00000064 : win32kbase!DrvDxgkCheckDisplayState+0x64
      ffff8789`4ead1a30 fffff803`580521f6     : ffff9501`21bdf000 ffff9501`21bdf000 ffffa58f`84de2ed0 00000000`00000000 : win32kbase!xxxDisplayDiagBlackScreenDetected+0x1b5
      ffff8789`4ead1b70 fffff803`58051c59     : ffff8789`4ead1cd8 ffffa58f`84de2ed0 ffff8789`4ead1cf0 ffffffff`ffffffff : win32kbase!DrvProcessWin32kEscape+0x58e
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: dxgkrnl
    IMAGE_NAME:  dxgkrnl.sys
    FAILURE_BUCKET_ID:  LKD_0x1B8_dxgkrnl!DxgCreateLiveDumpWithDriverBlob
```

### WATCHDOG4400-20260512-2038.dmp

```text
    PROCESS_NAME:  dwm.exe
    STACK_TEXT (top frames):
      ffff8789`4ead16a0 fffff803`53fb7128     : ffff9501`31ebdaa0 ffff9501`4b40f030 ffff9501`4b40f030 00000000`00000001 : watchdog!WdpDbgCaptureTriageDump+0xe6
      ffff8789`4ead1710 fffff803`53fb3b48     : ffffa58f`a3e0ec00 00000000`000001b8 00000000`00000001 ffff8789`4ead1840 : watchdog!WdDbgReportRecreate+0x108
      ffff8789`4ead1770 fffff803`5417a034     : ffffa58f`a3e0ece0 ffff8789`4ead1850 ffff9501`4b40f030 00000000`00000000 : watchdog!WdDbgReportCreate+0x58
      ffff8789`4ead17e0 fffff803`5418f4f6     : ffffa58f`a3e0e028 ffff9501`15c78000 00000000`00000000 fffff803`53fae9c8 : dxgkrnl!DxgCreateLiveDumpWithDriverBlob+0x154
      ffff8789`4ead1870 fffff803`5419022c     : ffffa58f`a3e0e000 ffffa58f`a3e0e000 00000000`00000000 00000000`000016f9 : dxgkrnl!DISPLAYDIAGNOSTICADAPTERDATA::CreateMiniportBlackboxLiveDump+0xca
      ffff8789`4ead18f0 fffff803`54190e28     : ffffa58f`00000000 ffffa58f`a3e0e000 00000000`00000000 fffff803`53fae9c8 : dxgkrnl!DISPLAYSTATECHECKER::LogAllDisplayDiagInfo+0xec
      ffff8789`4ead1950 fffff803`580e9adc     : ffff9501`114ef020 ffffa58f`84de2ed0 ffffa58f`84de2ed4 fffff803`c277118d : dxgkrnl!DxgkCheckDisplayState+0x118
      ffff8789`4ead19d0 fffff803`580caa55     : ffff9501`114ef020 fffff803`53ee6090 00000000`00000032 00000000`00000064 : win32kbase!DrvDxgkCheckDisplayState+0x64
      ffff8789`4ead1a30 fffff803`580521f6     : ffff9501`21bdf000 ffff9501`21bdf000 ffffa58f`84de2ed0 00000000`00000000 : win32kbase!xxxDisplayDiagBlackScreenDetected+0x1b5
      ffff8789`4ead1b70 fffff803`58051c59     : ffff8789`4ead1cd8 ffffa58f`84de2ed0 ffff8789`4ead1cf0 ffffffff`ffffffff : win32kbase!DrvProcessWin32kEscape+0x58e
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: dxgkrnl
    IMAGE_NAME:  dxgkrnl.sys
    FAILURE_BUCKET_ID:  LKD_0x1B8_dxgkrnl!DxgCreateLiveDumpWithDriverBlob
```

### WATCHDOG-20260512-2038.dmp

```text
    PROCESS_NAME:  dwm.exe
    STACK_TEXT (top frames):
      ffff8789`4ead16f0 fffff803`53fb7128     : ffff9501`31ebdaa0 00000000`00000000 00000000`00000000 00000000`00000001 : watchdog!WdpDbgCaptureTriageDump+0xe6
      ffff8789`4ead1760 fffff803`53fb3b48     : ffffa58f`a6a48000 00000000`000001a8 00000000`00000001 fffff803`5418f955 : watchdog!WdDbgReportRecreate+0x108
      ffff8789`4ead17c0 fffff803`5418f318     : ffffa58f`a6a48000 00000000`0002a618 00000000`00025cc1 00000000`00000000 : watchdog!WdDbgReportCreate+0x58
      ffff8789`4ead1830 fffff803`54190209     : ffffa58f`a3e0e000 ffffa58f`00000a60 00000000`00000001 00000000`000016f9 : dxgkrnl!DISPLAYSTATECHECKER::CreateBlackScreenLiveDump+0x444
      ffff8789`4ead18f0 fffff803`54190e28     : ffffa58f`00000000 ffffa58f`a3e0e000 00000000`00000000 fffff803`53fae9c8 : dxgkrnl!DISPLAYSTATECHECKER::LogAllDisplayDiagInfo+0xc9
      ffff8789`4ead1950 fffff803`580e9adc     : ffff9501`114ef020 ffffa58f`84de2ed0 ffffa58f`84de2ed4 fffff803`c277118d : dxgkrnl!DxgkCheckDisplayState+0x118
      ffff8789`4ead19d0 fffff803`580caa55     : ffff9501`114ef020 fffff803`53ee6090 00000000`00000032 00000000`00000064 : win32kbase!DrvDxgkCheckDisplayState+0x64
      ffff8789`4ead1a30 fffff803`580521f6     : ffff9501`21bdf000 ffff9501`21bdf000 ffffa58f`84de2ed0 00000000`00000000 : win32kbase!xxxDisplayDiagBlackScreenDetected+0x1b5
      ffff8789`4ead1b70 fffff803`58051c59     : ffff8789`4ead1cd8 ffffa58f`84de2ed0 ffff8789`4ead1cf0 ffffffff`ffffffff : win32kbase!DrvProcessWin32kEscape+0x58e
      ffff8789`4ead1c00 fffff803`543e9655     : ffffffff`ffffffff 00000000`00000000 ffffffff`ffffffff ffffb680`1fc270b0 : win32kbase!DxgkEngProcessWin32kEscape+0x9
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: dxgkrnl
    IMAGE_NAME:  dxgkrnl.sys
    FAILURE_BUCKET_ID:  LKD_0x1A8_KEYBD_HOTKEY_dxgkrnl!DISPLAYSTATECHECKER::CreateBlackScreenLiveDump
```

### WATCHDOG-20260512-2031.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff8789`4ae9e5f0 fffff803`c2307ad9     : ffff9501`5c7d4010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffff8789`4ae9eb20 fffff803`c26a4b2a     : 00000000`00000000 00000000`00000000 fffff803`c26e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      ffff8789`4ae9eb50 fffff803`c26a4929     : 00000000`00000003 ffff8789`4ae9ed30 ffff9501`5c7d4010 ffff9501`31a2d930 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffff8789`4ae9ebb0 fffff803`54339349     : ffffa58f`9eff24c0 ffffa58f`9eff24c0 ffffa58f`a93749b9 ffff9501`5c7d4010 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffff8789`4ae9ec30 fffff803`a5647616     : 00000000`00000006 00000000`00000006 ffff9501`15d4b000 ffff9501`15d51620 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffff8789`4ae9edf0 fffff803`a56a6a56     : ffff9501`15d4b000 ffff9501`15d4b000 ffff9501`15c7a000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffff8789`4ae9efe0 fffff803`a56b71af     : 00000000`00000000 00000000`00000000 ffff9501`15d4b000 ffff8789`4ae9f0f0 : dxgmms2!VidSchiResetEngines+0xea
      ffff8789`4ae9f030 fffff803`a57058ee     : ffff9501`15d4b000 ffffffff`feced300 ffff9501`15c7a000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffff8789`4ae9f0b0 fffff803`a563b41f     : 00000000`0000000c ffff9501`15c7a000 ffff9501`15d69000 fffff803`a5617178 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffff8789`4ae9f1a0 fffff803`a5710c4b     : 00000000`0000000e 00000000`00000014 00000000`0000000e ffff9501`15c7a000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260512-0039.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      ffff970e`fd68e5f0 fffff804`7b107ad9     : ffffd50d`6a9ca010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      ffff970e`fd68eb20 fffff804`7b4a4b2a     : 00000000`00000000 00000000`00000000 fffff804`7b4e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      ffff970e`fd68eb50 fffff804`7b4a4929     : 00000000`00000003 ffff970e`fd68ed30 ffffd50d`6a9ca010 ffffd50d`727787a0 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffff970e`fd68ebb0 fffff804`0cfd9349     : ffff9a0b`40067eb0 ffff9a0b`40067eb0 ffff9a0b`6b3767ef ffffd50d`6a9ca010 : nt!DbgkWerCaptureLiveKernelDump+0x69
      ffff970e`fd68ec30 fffff804`0fa47616     : 00000000`00000006 00000000`00000006 ffffd50d`45f03000 ffffd50d`45f0a6c0 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      ffff970e`fd68edf0 fffff804`0faa6a56     : ffffd50d`45f03000 ffffd50d`45f03000 ffffd50d`45e03000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      ffff970e`fd68efe0 fffff804`0fab71af     : 00000000`00000000 00000000`00000000 ffffd50d`45f03000 ffff970e`fd68f0f0 : dxgmms2!VidSchiResetEngines+0xea
      ffff970e`fd68f030 fffff804`0fb058ee     : ffffd50d`45f03000 ffffffff`feced300 ffffd50d`45e03000 00000000`00001000 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      ffff970e`fd68f0b0 fffff804`0fa3b41f     : 00000000`0000000c ffffd50d`45e03000 ffffd50d`45f1a000 ffff970e`fd68f308 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      ffff970e`fd68f1a0 fffff804`0fb10c4b     : 00000000`0000000e 00000000`00000014 00000000`0000000e ffffd50d`45e03000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260512-0008.dmp

```text
    PROCESS_NAME:  System
    STACK_TEXT (top frames):
      fffff585`9ad163b0 fffff807`83507ad9     : ffffde8e`622b3010 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveTriageDump+0x8e
      fffff585`9ad168e0 fffff807`838a4b2a     : 00000000`00000000 00000000`00000000 fffff807`838e9340 00000000`c0000099 : nt!DbgkpWerProcessPolicyResult+0x21
      fffff585`9ad16910 fffff807`838a4929     : 00000000`00000003 fffff585`9ad16af0 ffffde8e`622b3010 ffffde8e`910ac8b0 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      fffff585`9ad16970 fffff807`15449349     : ffffcb8e`20dcb640 ffffcb8e`20dcb640 ffffcb8e`583a5329 ffffde8e`622b3010 : nt!DbgkWerCaptureLiveKernelDump+0x69
      fffff585`9ad169f0 fffff807`66447616     : 00000000`00000006 00000000`00000006 ffffde8e`56cd8000 ffffde8e`56cde690 : dxgkrnl!TdrCollectDbgInfoStage1+0xd69
      fffff585`9ad16bb0 fffff807`664a6a56     : ffffde8e`56cd8000 ffffde8e`56cd8000 ffffde8e`56c32000 00000000`00000000 : dxgmms2!VidSchiResetEngine+0x36e
      fffff585`9ad16da0 fffff807`664b71af     : 00000000`00000000 00000000`00000000 ffffde8e`56cd8000 fffff585`9ad16eb0 : dxgmms2!VidSchiResetEngines+0xea
      fffff585`9ad16df0 fffff807`665058ee     : ffffde8e`56cd8000 ffffffff`feced300 ffffde8e`56c32000 00000000`00000001 : dxgmms2!VidSchWaitForCompletionEvent+0x37b
      fffff585`9ad16e70 fffff807`6643b41f     : 00000000`00000000 ffffde8e`56c32000 ffffde8e`56cd8000 fffff807`150f5519 : dxgmms2!VidSchiWaitForCompletePreemption+0x7e
      fffff585`9ad16f60 fffff807`66510c4b     : 00000000`00000000 00000000`00000014 00000000`00000000 ffffde8e`56c32000 : dxgmms2!VidSchiCompletePreemption+0x13
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

### WATCHDOG-20260514-2314.dmp

```text
    PROCESS_NAME:  Darktide.exe
    STACK_TEXT (top frames):
      ffffb189`f3cb65e0 fffff802`f29994d5     : 00000000`00000000 00000000`00000000 ffffb189`f3cb66b0 ffffd505`58510480 : nt!IopLiveDumpCollectPages+0xd9
      ffffb189`f3cb6630 fffff802`f2f61cff     : 00000000`00000000 ffffd505`58510480 ffffb189`f3cb66b0 00000000`00000002 : nt!IopLiveDumpEndMirroringCallback+0x55
      ffffb189`f3cb6660 fffff802`f2998afa     : 00000000`00000000 00000000`00000000 ffffd505`84b24ae0 ffffb189`f3cb6b28 : nt!MmDuplicateMemory+0x2e7
      ffffb189`f3cb66f0 fffff802`f2998bdc     : 00000000`00000000 fffff802`f2aabdb0 00000000`00000000 00000000`00000000 : nt!IopLiveDumpCapture+0x86
      ffffb189`f3cb6750 fffff802`f28e4140     : ffffd505`84b24ae0 ffffd505`84b24ae0 ffffd505`84b24f90 00000000`00000000 : nt!IopLiveDumpCaptureMemoryPages+0x50
      ffffb189`f3cb6890 fffff802`f2e960fd     : 00000000`00000000 ffffd505`8e27bd30 00000000`00000038 ffffb283`661d74c0 : nt!IoCaptureLiveDump+0x428
      ffffb189`f3cb6ac0 fffff802`f2b07ae8     : 00000000`00000000 00000000`00000000 00000000`00000000 00000000`00000000 : nt!DbgkpWerCaptureLiveFullDump+0x181
      ffffb189`f3cb6b20 fffff802`f2ea4b2a     : ffffb283`4b8641c4 00000000`00000000 00000000`00000000 fffff802`8480f786 : nt!DbgkpWerProcessPolicyResult+0x30
      ffffb189`f3cb6b50 fffff802`f2ea4929     : 00000000`00000001 ffffb189`f3cb6d30 ffffd505`846b6010 ffffd505`85d85180 : nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      ffffb189`f3cb6bb0 fffff802`849a9309     : ffffb283`661e8bb0 ffffb283`661e8bb0 ffffb283`4b8641c4 ffffd505`846b6010 : nt!DbgkWerCaptureLiveKernelDump+0x69
      (top 10 frames shown; cdb stack was longer)
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
```

