using Warp4D.UI;

namespace Warp4D;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Length>=2 && args[0].Equals("--nametable-pixel-snapshot-smoke",StringComparison.OrdinalIgnoreCase))
            return NametablePixelSnapshotTests.Run(args[1],args.Length>=3?args[2]:null);
        if(args.Length>=2 && args[0].Equals("--backdrop-cache-smoke",StringComparison.OrdinalIgnoreCase))
            return BackdropCacheTests.Run(args[1]);
        if(args.Length>=5 && args[0].Equals("--scene-cached-backdrop-equality",StringComparison.OrdinalIgnoreCase))
            return SceneArtworkEqualityTests.Run(args[1],args[2],args[3],args[4],combined:true,backdropCache:true);
        if(args.Length>=5 && args[0].Equals("--scene-run-backdrop-equality",StringComparison.OrdinalIgnoreCase))
            return SceneArtworkEqualityTests.Run(args[1],args[2],args[3],args[4],combined:true,runBackdrop:true);
        if(args.Length>=3 && args[0].Equals("--icarus-hud-bricks",StringComparison.OrdinalIgnoreCase))
            return IcarusHudBrickTests.Run(args[1],args[2]);
        if(args.Length>=3 && args[0].Equals("--icarus-fortress-hud",StringComparison.OrdinalIgnoreCase))
            return IcarusFortressHudTests.Run(args[1],args[2],args.Length>=4&&args[3]=="baseline");
        if(args.Length>=2 && args[0].Equals("--recording-mux-smoke",StringComparison.OrdinalIgnoreCase))
            return RecordingMuxTests.Run(args[1]);
        if(args.Length>=2 && args[0].Equals("--recording-demand-smoke",StringComparison.OrdinalIgnoreCase))
            return RecordingDemandTests.Run(args[1]);
        if(args.Length>=3 && args[0].Equals("--player-tracking-editor-smoke",StringComparison.OrdinalIgnoreCase))
            return PlayerTrackingEditorTests.Run(args[1],args[2]);
        if(args.Length>=3 && args[0].Equals("--sprite-assembly-editor-smoke",StringComparison.OrdinalIgnoreCase))
            return SpriteAssemblyEditorTests.Run(args[1],args[2]);
        if(args.Length>=5 && args[0].Equals("--gpu-byte-color-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPackingTests.Run(args[1],args[2],args[3],args[4],byteColorsOnly:true);
        if(args.Length>=3 && args[0].Equals("--smb-fire-fixtures",StringComparison.OrdinalIgnoreCase))
            return SmbFireFixtureTests.Run(args[1],args[2],args.Length>=4?args[3]:"world");
        if(args.Length>=3 && args[0].Equals("--smb-water-fixtures",StringComparison.OrdinalIgnoreCase))
            return SmbWaterFixtureTests.Run(args[1],args[2],args.Length>=4?args[3]:"world");
        if(args.Length>=3 && args[0].Equals("--embedded-profile-smoke",StringComparison.OrdinalIgnoreCase))
            return EmbeddedProfileTests.Run(args[1],args[2]);
        if(args.Length>=5 && args[0].Equals("--streaming-direct-performance",StringComparison.OrdinalIgnoreCase))
            return DirectGpuStageTests.RunPerformance(args[1],args[2],args[3],args[4],streamingOnly:true);
        if(args.Length>=2 && args[0].Equals("--streaming-gpu-smoke",StringComparison.OrdinalIgnoreCase))
            return DirectGpuStageTests.Run(args[1],textureOnly:true,streaming:true);
        if(args.Length>=5 && args[0].Equals("--streaming-frame-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.RunFrame(args[1],args[2],args[3],args[4],args.Length>=6?args[5]:null,streaming:true);
        if(args.Length>=2 && args[0].Equals("--geometry-cache-smoke",StringComparison.OrdinalIgnoreCase))
            return GeometryMapCacheTests.Run(args[1]);
        if(args.Length>=5 && args[0].Equals("--geometry-cache-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.RunAssembly(args[1],args[2],args[3],args[4],geometryCacheOnly:true);
        if(args.Length>=3 && args[0].Equals("--archive-frame-reader-smoke",StringComparison.OrdinalIgnoreCase))
            return ArchiveFrameReaderTests.Run(args[1],args[2]);
        if(args.Length>=5 && args[0].Equals("--mixed-smb-lifecycle",StringComparison.OrdinalIgnoreCase))
            return CoreLifecycleTests.Run(args[1],args[4],args.Length>=6?int.Parse(args[5]):2,args[2],args[3]);
        if(args.Length>=4 && args[0].Equals("--smb-natural-forms",StringComparison.OrdinalIgnoreCase))
            return SmbNaturalGrowthTests.Run(args[1],args[2],args[3],luigi:args.Contains("--luigi"),forms:true);
        if(args.Length>=2 && args[0].Equals("--d3d-primitive-display",StringComparison.OrdinalIgnoreCase))
            return D3dPrimitiveDisplayTests.Run(args[1]);
        if(args.Length>=2 && args[0].Equals("--gpu-primitive-display",StringComparison.OrdinalIgnoreCase))
            return GpuPrimitiveDisplayTests.Run(args[1]);
        if(args.Length>=2 && args[0].Equals("--gpu-primitive-dxgi-display",StringComparison.OrdinalIgnoreCase))
            return GpuPrimitiveDisplayTests.Run(args[1],gpuAware:true);
        if(args.Length>=2 && args[0].Equals("--gpu-dxgi-gdi-control",StringComparison.OrdinalIgnoreCase))
            return GpuPrimitiveDisplayTests.RunDxgiGdiControl(args[1]);
        if(args.Length>=4 && args[0].Equals("--smb-luigi-growth",StringComparison.OrdinalIgnoreCase))
            return SmbNaturalGrowthTests.Run(args[1],args[2],args[3],luigi:true);
        if(args.Length>=4 && args[0].Equals("--smb-two-player",StringComparison.OrdinalIgnoreCase))
            return SmbTwoPlayerTests.Run(args[1],args[2],args[3]);
        if(args.Length>=4 && args[0].Equals("--smb-natural-growth",StringComparison.OrdinalIgnoreCase))
            return SmbNaturalGrowthTests.Run(args[1],args[2],args[3]);
        if(args.Length>=4 && args[0].Equals("--smb-player-ownership",StringComparison.OrdinalIgnoreCase))
            return SmbPlayerOwnershipTests.Run(args[1],args[2],args[3]);
        if(args.Length>=4 && args[0].Equals("--smb-paired-alignment",StringComparison.OrdinalIgnoreCase))
            return SmbPairedAlignmentTests.Run(args[1],args[2],args[3]);
        if(args.Length>=4 && args[0].Equals("--smb-attract-transition",StringComparison.OrdinalIgnoreCase))
            return SmbAttractTransitionTests.Run(args[1],args[2],args[3],storeFrames:!args.Contains("--pictures-only"));
        if(args.Length>=4 && args[0].Equals("--smb-terrain-catalog",StringComparison.OrdinalIgnoreCase))
            return SmbTerrainCatalogTests.Run(args[1],args[2],args[3]);
        if(args.Length>=4 && args[0].Equals("--famidash-paired-later-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.Run(args[1],args[2],args[3],wider:true,levels:[20,23,26],nativeReference:true,requirePaired:true,sampleCount:4);
        if(args.Length>=5 && args[0].Equals("--famidash-paired-later-render-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunBankRender(args[1],args[2],args[3],args[4],expectedFrames:24,requirePaired:true);
        if(args.Length>=5 && args[0].Equals("--famidash-committed-alignment",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunPairedAlignment(args[1],args[2],args[3],args[4],committedViewport:true);
        if(args.Length>=5 && args[0].Equals("--famidash-paired-alignment",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunPairedAlignment(args[1],args[2],args[3],args[4]);
        if(args.Length>=5 && args[0].Equals("--famidash-paired-render-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunBankRender(args[1],args[2],args[3],args[4],expectedFrames:48,requirePaired:true);
        if(args.Length>=4 && args[0].Equals("--famidash-paired-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.Run(args[1],args[2],args[3],wider:true,levels:[12,18],nativeReference:true,requirePaired:true);
        if(args.Length>=5 && args[0].Equals("--famidash-alias-guards",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunAliasGuards(args[1],args[2],args[3],args[4]);
        if(args.Length>=5 && args[0].Equals("--famidash-palette-audit",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunPaletteAudit(args[1],args[2],args[3],args[4]);
        if(args.Length>=4 && args[0].Equals("--famidash-middle-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.Run(args[1],args[2],args[3],wider:true,levels:[8,12,18,24],nativeReference:true);
        if(args.Length>=5 && args[0].Equals("--famidash-later-render-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunBankRender(args[1],args[2],args[3],args[4],expectedFrames:72);
        if(args.Length>=6 && args[0].Equals("--famidash-editor-upgrade-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunEditorUpgrade(args[1],args[2],args[3],args[4],args[5]);
        if(args.Length>=4 && args[0].Equals("--famidash-later-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.Run(args[1],args[2],args[3],wider:true,levels:[20,23,26],nativeReference:true);
        if(args.Length>=5 && args[0].Equals("--famidash-bank-render-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunBankRender(args[1],args[2],args[3],args[4]);
        if(args.Length>=5 && args[0].Equals("--famidash-artwork-audit",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunArtworkAudit(args[1],args[2],args[3],args[4]);
        if(args.Length>=2 && args[0].Equals("--background-lock-performance",StringComparison.OrdinalIgnoreCase))
            return BackgroundLockTests.Run(args[1]);
        if(args.Length>=2 && args[0].Equals("--backdrop-counting-performance",StringComparison.OrdinalIgnoreCase))
            return BackdropCountingTests.Run(args[1]);
        if(args.Length>=2 && args[0].Equals("--backdrop-visibility-performance",StringComparison.OrdinalIgnoreCase))
            return BackdropVisibilityTests.Run(args[1]);
        if(args.Length>=2 && args[0].Equals("--combined-artwork-performance",StringComparison.OrdinalIgnoreCase))
            return CombinedArtworkTests.Run(args[1]);
        if(args.Length>=3 && args[0].Equals("--scene-artwork-regression",StringComparison.OrdinalIgnoreCase))
            return ArtworkRegressionTests.Run(args[1],args[2]);
        if(args.Length>=5 && args[0].Equals("--scene-artwork-equality",StringComparison.OrdinalIgnoreCase))
            return SceneArtworkEqualityTests.Run(args[1],args[2],args[3],args[4],combined:args.Length>=6&&args[5]=="combined");
        if(args.Length>=2 && args[0].Equals("--background-shared-lock-performance",StringComparison.OrdinalIgnoreCase))
            return BackgroundLockTests.Run(args[1],sharedOnly:true);
        if(args.Length>=2 && args[0].Equals("--sprite-decode-smoke",StringComparison.OrdinalIgnoreCase))
            return SpriteDecodeTests.Run(args[1]);
        if(args.Length>=2 && args[0].Equals("--sprite-preflight-smoke",StringComparison.OrdinalIgnoreCase))
            return SpritePreflightTests.Run(args[1]);
        if(args.Length>=3 && args[0].Equals("--scene-sprite-preflight-regression",StringComparison.OrdinalIgnoreCase))
            return ArtworkRegressionTests.Run(args[1],args[2],spritePreflight:true,requireDefault:args.Length>=4&&args[3]=="default");
        if(args.Length>=5 && args[0].Equals("--gpu-tint-cache-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPackingTests.Run(args[1],args[2],args[3],args[4],tintOnly:true);
        if(args.Length>=5 && args[0].Equals("--gpu-packing-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPackingTests.Run(args[1],args[2],args[3],args[4]);
        if(args.Length>=5 && args[0].Equals("--native-playback-performance",StringComparison.OrdinalIgnoreCase))
            return NativePlaybackPerformanceTests.Run(args[1],args[2],args[3],int.Parse(args[4]));
        if(args.Length>=5 && args[0].Equals("--scene-build-reuse-smoke",StringComparison.OrdinalIgnoreCase))
            return SceneBuildReuseTests.Run(args[1],args[2],args[3],args[4]);
        if(args.Length>=3 && args[0].Equals("--scene-build-live-reuse-smoke",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.RunLive(args[1],args[2],verifySceneReuse:true);
        if(args.Length>=2 && args[0].Equals("--vertical-tracking-smoke",StringComparison.OrdinalIgnoreCase))
            return VerticalTrackingTests.Run(args[1]);
        if(args.Length>=2 && args[0].Equals("--hud-player-layer-smoke",StringComparison.OrdinalIgnoreCase))
            return HudPlayerLayerTests.Run(args[1]);
        if(args.Length>=3 && args[0].Equals("--profile-scene-diff",StringComparison.OrdinalIgnoreCase))
            return ProfileSceneDiffTests.Run(args[1],args[2]);
        if(args.Length>=5 && args[0].Equals("--sheet-descriptor-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.RunAssembly(args[1],args[2],args[3],args[4],descriptorsOnly:true);
        if(args.Length>=2 && args[0].Equals("--icarus-training-controller-smoke",StringComparison.OrdinalIgnoreCase))
            return IcarusTrainingControllerTests.Run(args[1]);
        if(args.Length>=3 && args[0].Equals("--icarus-player-smoke",StringComparison.OrdinalIgnoreCase))
            return IcarusPlayerTests.Run(args[1],args[2]);
        if(args.Length>=3 && args[0].Equals("--icarus-chamber-smoke",StringComparison.OrdinalIgnoreCase))
            return IcarusChamberTests.Run(args[1],args[2]);
        if(args.Length>=3 && args[0].Equals("--visibility-performance-smoke",StringComparison.OrdinalIgnoreCase))
            return VisibilityPerformanceTests.Run(args[1],args[2]);
        if(args.Length>=3 && args[0].Equals("--fingerprint-performance-smoke",StringComparison.OrdinalIgnoreCase))
            return FingerprintPerformanceTests.Run(args[1],args[2]);
        if(args.Length>=2 && args[0].Equals("--gpu-visible-stage-diagnostic",StringComparison.OrdinalIgnoreCase))
            return DirectGpuStageTests.Run(args[1],visibleDiagnostic:true);
        if(args.Length>=2 && args[0].Equals("--gpu-texture-smoke",StringComparison.OrdinalIgnoreCase))
            return DirectGpuStageTests.Run(args[1],textureOnly:true);
        if(args.Length>=3 && args[0].Equals("--castlevania-profile-smoke",StringComparison.OrdinalIgnoreCase))
            return CastlevaniaProfileTests.Run(args[1],args[2]);
        if(args.Length>=3 && args[0].Equals("--sprite-assembly-smoke",StringComparison.OrdinalIgnoreCase))
            return SpriteAssemblyTests.Run(args[1],args[2]);
        if(args.Length>=3 && args[0].Equals("--metroid-profile-smoke",StringComparison.OrdinalIgnoreCase))
            return MetroidProfileTests.Run(args[1],args[2]);
        if(args.Length>=3 && args[0].Equals("--tetris-profile-smoke",StringComparison.OrdinalIgnoreCase))
            return TetrisProfileTests.Run(args[1],args[2]);
        if(args.Length>=2 && args[0].Equals("--native-chr-classification-smoke",StringComparison.OrdinalIgnoreCase))
            return CoreLifecycleTests.RunChrClassification(args[1]);
        if(args.Length>=3 && args[0].Equals("--core-lifecycle-stress",StringComparison.OrdinalIgnoreCase))
            return CoreLifecycleTests.Run(args[1],args[2],args.Length>=4?int.Parse(args[3]):8);
        if(args.Length>=2 && args[0].Equals("--direct-gpu-stage-smoke",StringComparison.OrdinalIgnoreCase))
            return DirectGpuStageTests.Run(args[1]);
        if(args.Length>=2 && args[0].Equals("--frame-delivery-smoke",StringComparison.OrdinalIgnoreCase))
            return FrameDeliveryTests.Run(args[1]);
        if(args.Length>=5 && args[0].Equals("--direct-gpu-stage-performance",StringComparison.OrdinalIgnoreCase))
            return DirectGpuStageTests.RunPerformance(args[1],args[2],args[3],args[4]);
        if(args.Length>=5 && args[0].Equals("--surface-light-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.RunLighting(args[1],args[2],args[3],args[4]);
        if(args.Length>=5 && args[0].Equals("--surface-assembly-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.RunAssembly(args[1],args[2],args[3],args[4]);
        if(args.Length>=5 && args[0].Equals("--surface-order-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.RunOrdering(args[1],args[2],args[3],args[4]);
        if(args.Length>=2 && args[0].Equals("--surface-reuse-smoke",StringComparison.OrdinalIgnoreCase))
            return SurfaceReuseTests.Run(args[1]);
        if(args.Length>=5 && args[0].Equals("--gpu-frame-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.RunFrame(args[1],args[2],args[3],args[4],args.Length>=6?args[5]:null);
        if (args.Length >= 5 && args[0].Equals("--masked-edge-smoke", StringComparison.OrdinalIgnoreCase))
            return MaskedEdgeArtworkTests.Run(args[1],args[2],args[3],args[4]);
        if (args.Length >= 3 && args[0].Equals("--profile-coverage", StringComparison.OrdinalIgnoreCase))
            return ProfileCoverageTests.Run(args[1], args[2]);
        if(args.Length>=3 && args[0].Equals("--gpu-gameplay-smoke",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.RunLive(args[1],args[2]);
        if(args.Length>=3 && args[0].Equals("--gpu-performance",StringComparison.OrdinalIgnoreCase))
            return GpuPerformanceTests.Run(args[1],args[2]);
        if(args.Length>=2&&args[0].Equals("--friendly-ui-smoke",StringComparison.OrdinalIgnoreCase))
            return FriendlyUiTests.Run(args[1],args.Length>=3?args[2]:null);
        if(args.Length>=2 && args[0].Equals("--dimension-smoke",StringComparison.OrdinalIgnoreCase))
            return DimensionTests.Run(args[1],args.Length>=3?args[2]:null);
        if (args.Length >= 2 && args[0].Equals("--wow-smoke", StringComparison.OrdinalIgnoreCase))
            return WowTests.Run(args[1]);
        if (args.Length >= 2 && args[0].Equals("--cycle-interaction-smoke", StringComparison.OrdinalIgnoreCase))
            return CycleInteractionTests.Run(args[1]);
        if (args.Length >= 4 && args[0].Equals("--profiles-smoke", StringComparison.OrdinalIgnoreCase))
            return BuiltInProfileTests.Run(args[1],args[2],args[3]);
        if (args.Length >= 3 && args[0].Equals("--profile-author", StringComparison.OrdinalIgnoreCase))
            return ProfileAuthoring.Run(args[1],args[2]);
        if (args.Length >= 2 && args[0].Equals("--geometry-smoke", StringComparison.OrdinalIgnoreCase))
            return GeometryTests.Run(args[1]);
        if (args.Length >= 4 && args[0].Equals("--profile-capture", StringComparison.OrdinalIgnoreCase))
            return ProfileCalibration.Run(args[1], args[2], args[3]);
        if(args.Length>=4 && args[0].Equals("--famidash-route-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.Run(args[1],args[2],args[3],wider:true);
        if(args.Length>=4 && args[0].Equals("--famidash-level-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.Run(args[1],args[2],args[3]);
        if(args.Length>=5 && args[0].Equals("--famidash-loading-check",StringComparison.OrdinalIgnoreCase))
            return FamiDashLevelTests.RunLoading(args[1],args[2],args[3],args[4]);
        if (args.Length >= 4 && args[0].Equals("--famidash-profile-smoke", StringComparison.OrdinalIgnoreCase))
            return FamiDashProfileTests.Run(args[1], args[2], args[3]);
        if (args.Length >= 4 && args[0].Equals("--smb-smoke", StringComparison.OrdinalIgnoreCase))
            return SmbRegressionTests.Run(args[1], args[2], args[3]);
        if (args.Length >= 2 && args[0].Equals("--frontend-smoke", StringComparison.OrdinalIgnoreCase))
            return FrontendTests.Run(args[1]);
        if (args.Length >= 4 && args[0].Equals("--upgrade-smoke", StringComparison.OrdinalIgnoreCase))
        {
            return UpgradeTests.Run(args[1], args[2], args[3]);
        }
        if (args.Length >= 3 && args[0].Equals("--smoke", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeTest.Run(args[1], args[2]);
        }
        if (args.Length >= 3 && args[0].Equals("--attract-smoke", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeTest.RunAttractDemo(args[1], args[2]);
        }
        if (args.Length >= 3 && args[0].Equals("--game-profile-smoke", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeTest.RunGameProfile(args[1], args[2]);
        }
        if (args.Length >= 3 && args[0].Equals("--scroll-smoke", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeTest.RunScrollCapture(args[1], args[2]);
        }
        Application.Run(new MainForm());
        return 0;
    }
}
