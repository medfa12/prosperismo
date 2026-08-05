// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SharpEmu.GUI;
using SharpEmu.GUI.BootAnimation;
using SharpEmu.GUI.Controls;
using SharpEmu.GUI.Ps5Home;
using SharpEmu.GUI.SystemAssets;

namespace ShellShot;

/// <summary>
/// Renders the shell's real controls off-screen and writes png frames.
///
/// The point is being able to look at what shipped. A control can pass every
/// numeric test and still be laid out wrong, and the screen-capture route does
/// not work from a session with no interactive desktop, so this drives Avalonia
/// headless with the Skia renderer and pulls frames straight out of the
/// compositor. Same controls, same styles, no windowing toolkit.
///
///   dotnet run --project tools/shell-shot -- --out shots --scene tilerow
///   dotnet run --project tools/shell-shot -- --out shots --scene entrance --frames 12 --step 150
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options is null)
        {
            Console.WriteLine(Options.Usage);
            return 1;
        }

        // Process-local only: all existing shell asset services resolve the
        // same user-owned dump while this capture is alive. Nothing is copied
        // into the output directory except the rendered PNG.
        if (!string.IsNullOrWhiteSpace(options.FirmwareRoot))
        {
            Environment.SetEnvironmentVariable(
                RnpsShellAssets.DumpEnvironmentVariable,
                options.FirmwareRoot);
        }
        if (!string.IsNullOrWhiteSpace(options.HomeSource))
        {
            Environment.SetEnvironmentVariable(
                Ps5HomeSourceBundle.PathOverrideVariable,
                options.HomeSource);
        }

        Directory.CreateDirectory(options.Output);

        BuildAvaloniaApp().SetupWithoutStarting();

        if (string.Equals(options.Scene, "focus-idle", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(options.FirmwareRoot))
        {
            var focusNoise = ShellFocusRingTimeline.FocusNoiseTexture;
            if (focusNoise is null)
            {
                Console.Error.WriteLine(
                    "focus-idle: firmware root was supplied, but image_focus_noise could not be loaded from Sce.PlayStation.PUI_UI3.rco");
                return 2;
            }

            Console.WriteLine("focus-idle: loaded firmware image_focus_noise ({0} bytes)", focusNoise.Length);
        }

        var window = new Window
        {
            Width = options.Width,
            Height = options.Height,
            SystemDecorations = SystemDecorations.None,
            Background = new SolidColorBrush(Color.FromRgb(2, 4, 8)),
        };

        var scene = Scenes.Build(options.Scene, options);
        window.Content = scene.Root;
        window.Show();

        // One tick to get through the first layout pass before anything is
        // measured or captured.
        Pump(window, TimeSpan.FromMilliseconds(16));

        // Firmware wallpaper decoding and the console's delayed title reveal
        // are asynchronous. Give the real controls wall-clock time to finish
        // instead of recording a black pre-load frame and calling it the UI.
        if (string.Equals(options.Scene, "firmware-home", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Scene, "native-background", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Scene, "native-background-bottom", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(options.Scene, "focus-idle", StringComparison.OrdinalIgnoreCase) &&
             !string.IsNullOrWhiteSpace(options.FirmwareRoot)) ||
            string.Equals(options.Scene, "settings", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Scene, "settings-detail", StringComparison.OrdinalIgnoreCase))
        {
            var warmup = Stopwatch.StartNew();
            var warmupDuration = string.Equals(
                options.Scene,
                "firmware-home",
                StringComparison.OrdinalIgnoreCase)
                ? TimeSpan.FromMilliseconds(1500)
                : string.Equals(options.Scene, "focus-idle", StringComparison.OrdinalIgnoreCase)
                    ? TimeSpan.FromMilliseconds(2500)
                    : TimeSpan.FromMilliseconds(5000);
            while (warmup.Elapsed < warmupDuration)
            {
                Thread.Sleep(10);
                scene.Advance?.Invoke(TimeSpan.FromMilliseconds(10));
                Pump(window, TimeSpan.FromMilliseconds(10));
            }
        }

        for (int i = 0; i < options.Frames; i++)
        {
            double atMs = i * options.StepMs;

            // Advance a frame at a time rather than in one jump. The controls'
            // springs clamp a single advance to 64 ms on purpose, so that a
            // stalled UI thread makes them arrive rather than teleport; feeding
            // them the whole step at once would quietly run them slow.
            if (i > 0)
            {
                double remaining = options.StepMs;
                while (remaining > 0.0)
                {
                    double slice = Math.Min(1000.0 / 60.0, remaining);
                    scene.Advance?.Invoke(TimeSpan.FromMilliseconds(slice));
                    remaining -= slice;
                }
            }
            else
            {
                scene.Advance?.Invoke(TimeSpan.Zero);
            }

            // Some headless backends cache a child image even after its
            // WriteableBitmap is replaced. Invalidate the hosted scene as well
            // so a capture always reflects the timestamp just advanced to.
            scene.Root.InvalidateVisual();
            Pump(window, TimeSpan.FromMilliseconds(16));
            if (scene.IsFramePending is not null)
            {
                var frameWait = Stopwatch.StartNew();
                while (scene.IsFramePending() && frameWait.Elapsed < TimeSpan.FromSeconds(2))
                {
                    Thread.Sleep(5);
                    Pump(window, TimeSpan.FromMilliseconds(5));
                }
                scene.Root.InvalidateVisual();
                Pump(window, TimeSpan.FromMilliseconds(16));
            }

            var frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                Console.Error.WriteLine("frame {0}: nothing captured", i);
                continue;
            }

            string path = Path.Combine(
                options.Output,
                string.Format(CultureInfo.InvariantCulture, "{0}_{1:0000}ms.png", options.Scene, (int)atMs));
            using (var fs = File.Create(path))
            {
                frame.Save(fs);
            }

            Console.WriteLine("{0}  {1}x{2}", path, frame.PixelSize.Width, frame.PixelSize.Height);
        }

        return 0;
    }

    private static void Pump(Window window, TimeSpan delta)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ShotApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://Prosperismo.Shell/Assets/Fonts#Fira Sans",
            });
}

internal sealed class ShotApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }
}

internal sealed record Scene(
    Control Root,
    Action<TimeSpan>? Advance,
    Func<bool>? IsFramePending = null);

internal static class Scenes
{
    public static Scene Build(string name, Options options) => name switch
    {
        "entrance" => Entrance(options),
        "marquee" => Marquee(options),
        "home" => Home(options),
        "focus" => Focus(options),
        "focus-idle" => FocusIdle(options),
        "list" => FocusList(options),
        "navband" => FocusNavBand(options),
        "panel" => FunctionPanel(options),
        "hub" => Hub(options),
        "backdrop" => Backdrop(options),
        "native-background" => NativeBackground(options),
        "native-background-bottom" => NativeBackground(options, bottom: true),
        "wave-background" => WaveBackground(options, highContrast: false),
        "high-contrast-background" => WaveBackground(options, highContrast: true),
        "theme-one-background" => WaveBackground(options, highContrast: false, themeColourIndex: 0x01),
        "firmware-home" => FirmwareHome(options),
        "settings" => Settings(options),
        "settings-detail" => SettingsDetail(options),
        "all-games" => AllGames(options),
        "boot" => Boot(options),
        _ => TileRow(options),
    };

    /// <summary>
    /// A fixed focus target with only the recovered line and area passes moving.
    /// This separates idle shader motion from layout, selection and entrance
    /// animations, so two captures can prove that a stationary ring is still
    /// consuming Sony's noise/shimmer clocks.
    /// </summary>
    private static Scene FocusIdle(Options options)
    {
        var root = new Panel
        {
            Width = Ps5DesignSpace.Width,
            Height = Ps5DesignSpace.Height,
            Background = new SolidColorBrush(Color.FromRgb(2, 4, 8)),
        };
        var ring = new ShellFocusRing
        {
            Width = Ps5DesignSpace.Width,
            Height = Ps5DesignSpace.Height,
            ManualClock = true,
            Radius = 32.0,
            LineScale = 1.0,
        };
        root.Children.Add(ring);
        ring.ShowAt(new Rect(720, 380, 480, 320));

        return new Scene(root, ring.Advance, () => ring.NativeFramePending);
    }

    private static Scene Boot(Options options)
    {
        var settings = new GuiSettings
        {
            PlayBootIntro = true,
            HasPlayedBootIntro = false,
            PlayShellMusic = false,
            PlayUiSounds = false,
        };
        var overlay = BootIntroOverlay.TryCreate(settings)
                      ?? throw new InvalidOperationException("boot intro did not arm");
        var elapsed = TimeSpan.Zero;
        return new Scene(overlay, delta =>
        {
            elapsed += delta;
            overlay.AdvanceForCapture(
                elapsed,
                delta,
                new Size(options.Width, options.Height));
        });
    }

    private static Scene NativeBackground(Options options, bool bottom = false)
    {
        var smallCache = Environment.GetEnvironmentVariable(
            SharpEmu.GUI.SystemAssets.Shell.Ps5NativeSmallParticleCacheFrameSource
                .CacheEnvironmentVariable);
        var background = new SharpEmu.GUI.SystemAssets.Shell.ShellBackground
        {
            IsMotionEnabled = true,
            GlobalState = string.IsNullOrWhiteSpace(smallCache)
                ? SharpEmu.GUI.SystemAssets.Shell.ShellGlobalBackgroundState.ColdBootAnimation
                : bottom
                    ? SharpEmu.GUI.SystemAssets.Shell.ShellGlobalBackgroundState.Login
                    : SharpEmu.GUI.SystemAssets.Shell.ShellGlobalBackgroundState.Shutdown,
        };
        background.NativeParticles.ManualClock = true;

        var reportedNativeFrame = false;
        void Advance(TimeSpan delta)
        {
            background.NativeParticles.AdvanceForCapture(delta);
            if (!reportedNativeFrame && background.NativeParticles.IsFrameLoaded)
            {
                reportedNativeFrame = true;
                Console.WriteLine("native particle frame loaded for raw state {0}",
                    SharpEmu.GUI.SystemAssets.Shell.ShellBackgroundComposition
                        .NativeParticleRouteFor(background.GlobalState).RawState);
            }
        }

        return new Scene(background, Advance, () => background.NativeParticles.LiveRenderPending);
    }

    private static Scene WaveBackground(
        Options options,
        bool highContrast,
        int themeColourIndex =
            SharpEmu.GUI.SystemAssets.Shell.Ps5NativeWavePlateEvaluator.SteadyNoParticleThemeIndex)
    {
        var background = new SharpEmu.GUI.SystemAssets.Shell.ShellBackground
        {
            IsMotionEnabled = true,
            HighContrast = highContrast,
            ThemeColourIndex = themeColourIndex,
        };

        return new Scene(background, _ => background.NativeWave.AdvanceFrame());
    }

    private static Scene Settings(Options options)
    {
        var settings = new ShellSettingsCategoryList();
        var background = new SharpEmu.GUI.SystemAssets.Shell.ShellBackground();
        var root = new Panel { Width = Ps5DesignSpace.Width, Height = Ps5DesignSpace.Height };
        root.Children.Add(background);
        root.Children.Add(settings);
        // Deliberately do not advance NativeWave by hand. This scene verifies
        // that the same RequestAnimationFrame route used by the real Settings
        // surface keeps the firmware phase moving after attachment.
        return new Scene(root, _ => settings.Focus());
    }

    private static Scene SettingsDetail(Options options)
    {
        var settings = new ShellSettingsDetailList();
        // Model a real category-route entry. TabbedList sets initialFocusTab
        // and then transfers focus into the mounted content panel.
        settings.SelectedTabIndex = 0;
        var background = new SharpEmu.GUI.SystemAssets.Shell.ShellBackground();
        var root = new Panel { Width = Ps5DesignSpace.Width, Height = Ps5DesignSpace.Height };
        root.Children.Add(background);
        root.Children.Add(settings);
        return new Scene(root, _ => settings.Focus());
    }

    /// <summary>NPXS40071's recovered installed-content grid on the same
    /// animated background used by the production Sony surface.</summary>
    private static Scene AllGames(Options options)
    {
        var games = new ShellAllGames
        {
            Width = Ps5DesignSpace.Width,
            Height = Ps5DesignSpace.Height,
            Title = "Game Library",
            Items = Enumerable.Range(1, Math.Max(1, options.Tiles))
                .Select(index => new ShellLibraryItem($"Installed Game {index}")
                {
                    SubLabel = $"{18 + index * 3}.2 GB",
                    SizeBytes = (18L + index * 3) << 30,
                    InstalledAt = DateTime.Today.AddDays(-index),
                })
                .ToArray(),
            IsRegionFocused = true,
        };

        var root = new Panel { Width = Ps5DesignSpace.Width, Height = Ps5DesignSpace.Height };
        root.Children.Add(new SharpEmu.GUI.SystemAssets.Shell.ShellBackground { IsMotionEnabled = true });
        root.Children.Add(games);

        var elapsed = TimeSpan.Zero;
        bool moved = false;
        return new Scene(root, delta =>
        {
            elapsed += delta;
            games.Focus();
            if (!moved && elapsed >= options.MoveAt)
            {
                moved = true;
                games.MoveFocus(ShellFocusDirection.Right);
            }

            if (ShellFocusRing.For(games) is { } ring)
            {
                ring.ManualClock = true;
                ring.Advance(delta);
            }
        });
    }

    /// <summary>
    /// First external-firmware visualization. The wallpaper and system-app
    /// icons are read in place from the user's 4.03 dump; Base/BGLayer are
    /// opened as one resource pack, with Base's default-game art as a fallback.
    /// BGLayer is reported but not drawn because its eight 4.03 entries are
    /// VR/gaze furniture rather than the normal Home background.
    /// </summary>
    private static Scene FirmwareHome(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.FirmwareRoot))
        {
            throw new ArgumentException("firmware-home requires --firmware-root <dump-root>");
        }

        var pack = Ps5ShellResourcePack.TryOpen(options.FirmwareRoot)
            ?? throw new InvalidOperationException("NPXS40087 Base/BGLayer resource pair was not found");
        var source = Ps5HomeSourceBundle.TryLocate(Array.Empty<string?>());

        Console.WriteLine("firmware root: {0}", Path.GetFullPath(options.FirmwareRoot));
        Console.WriteLine("Base.rco: {0} entries ({1})", pack.BaseEntryCount, pack.BasePath);
        Console.WriteLine("BGLayer.rco: {0} entries ({1}; VR/gaze only)", pack.BgLayerEntryCount, pack.BgLayerPath);
        Console.WriteLine("NPXS40002 source reference: {0}", source?.Path ?? "<not supplied>");

        var ownedBitmaps = new List<Bitmap>();
        var tiles = new List<ShellTile>();
        foreach (var app in RnpsShellAssets.EnumerateShellIcons(options.FirmwareRoot).Take(ShellTileRow.MaxTiles))
        {
            try
            {
                var bitmap = new Bitmap(app.IconPath);
                ownedBitmaps.Add(bitmap);
                tiles.Add(new ShellTile(RnpsShellAssets.ReadShellTitle(app), icon: bitmap));
            }
            catch (Exception exception) when (exception is IOException or ArgumentException)
            {
                // Continue with the other firmware icons.
            }
        }

        if (tiles.Count == 0 && pack.TryLoadBaseBitmap("tex_default_game") is { } fallback)
        {
            ownedBitmaps.Add(fallback);
            tiles.Add(new ShellTile("Game", icon: fallback));
        }

        var row = BuildRow(tiles);
        var strandHost = new Panel { Height = ShellTileRow.ScaledExperienceSize };
        strandHost.Children.Add(row);

        var band = new ShellNavBand();
        band.SetClockText("21:45");

        var page = new Grid { RowDefinitions = new RowDefinitions("126,168,*") };
        Grid.SetRow(band, 0);
        page.Children.Add(band);
        Grid.SetRow(strandHost, 1);
        page.Children.Add(strandHost);

        // Host the same recovered composite as production Home. The previous
        // firmware-home scene still instantiated Ps5BackgroundPlate directly,
        // bypassing Plane2 entirely; when bg_hub_default.dds was absent or not
        // decoded that made the visual QA scene uniformly black even though
        // the production shell had moved to ShellBackground.
        var background = new SharpEmu.GUI.SystemAssets.Shell.ShellBackground
        {
            DumpRootOverride = options.FirmwareRoot,
            GlobalState = SharpEmu.GUI.SystemAssets.Shell.ShellGlobalBackgroundState.NoParticle,
            IsMotionEnabled = true,
        };

        var root = new Panel();
        root.Children.Add(background);
        root.Children.Add(page);

        return new Scene(root, row.Advance);
    }

    /// <summary>
    /// The home background following the highlight: the plate starts on one
    /// title's own artwork and runs HOME's Normal-degree SlideInLeft program
    /// when the focus moves right to the next tile.
    ///
    /// <para>Pass <c>--art-a</c> and <c>--art-b</c> the two titles' <c>sce_sys</c>
    /// folders. A folder that ships no <c>pic</c> is the point of the exercise
    /// rather than a mistake: the plate is supposed to fall back to
    /// <c>bg_hub_default.dds</c> for it, and the only way to show that it does
    /// is to focus one.</para>
    ///
    /// <para>The native image program runs on a <see cref="DispatcherTimer"/>, so this scene
    /// sleeps for the frame's worth of wall-clock time rather than pretending
    /// to advance a manual clock. That makes the captured frames real samples
    /// of the animation instead of a reconstruction of it. The scene also holds
    /// its first capture until the initial DDS has decoded, then holds the move
    /// frame until the second decode has actually started the native program;
    /// otherwise asynchronous image loading records black frames and spends the
    /// transition before there are two textures to compare.</para>
    /// </summary>
    private static Scene Backdrop(Options options)
    {
        var plate = new Ps5BackgroundPlate();

        var band = new ShellNavBand();
        band.SetClockText("21:45");

        var row = BuildRow(options.Tiles);
        var strandHost = new Panel { Height = ShellTileRow.ScaledExperienceSize };
        strandHost.Children.Add(row);

        var page = new Grid { RowDefinitions = new RowDefinitions("126,168,*") };
        Grid.SetRow(band, 0);
        page.Children.Add(band);
        Grid.SetRow(strandHost, 1);
        page.Children.Add(strandHost);

        var root = new Panel();
        root.Children.Add(plate);
        root.Children.Add(page);

        var first = Ps5TitleArtwork.ResolveBackdrop(options.ArtA);
        var second = Ps5TitleArtwork.ResolveBackdrop(options.ArtB);
        Console.WriteLine("backdrop A: {0}", first ?? "<none - falls back to bg_hub_default>");
        Console.WriteLine("backdrop B: {0}", second ?? "<none - falls back to bg_hub_default>");

        plate.TitleArtPath = first;
        plate.ConfigureNativeImageTransition(
            SharpEmu.GUI.SystemAssets.Shell.ShellLayerBackgroundTransitionType.CustomImageSlideInLeft,
            SharpEmu.GUI.SystemAssets.Shell.ShellLayerBackgroundTransitionDegree.Normal);

        var elapsed = TimeSpan.Zero;
        var moved = false;

        return new Scene(
            root,
            delta =>
            {
                elapsed += delta;
                if (!moved && elapsed >= options.MoveAt)
                {
                    moved = true;
                    Console.WriteLine("focus moves at {0:0} ms", elapsed.TotalMilliseconds);
                    plate.TitleArtPath = second;
                }

                // Real time, because the native image program is driven by a
                // real render-priority timer.
                if (delta > TimeSpan.Zero)
                {
                    Thread.Sleep(delta);
                }

                Dispatcher.UIThread.RunJobs();
                row.Advance(delta);
            },
            () => plate.IsImageLoadPending);
    }

    /// <summary>
    /// The hub open over the home: the whole home surface lifted by 166 with
    /// the switcher faded out, and the hub's header at its own inset.
    /// </summary>
    private static Scene Hub(Options options)
    {
        var row = BuildRow(options.Tiles);
        var strandHost = new Panel { Height = ShellTileRow.ScaledExperienceSize };
        strandHost.Children.Add(row);

        var band = new ShellNavBand();
        band.SetClockText("21:45");

        var page = new Grid { RowDefinitions = new RowDefinitions("126,168,*") };
        Grid.SetRow(band, 0);
        page.Children.Add(band);
        Grid.SetRow(strandHost, 1);
        page.Children.Add(strandHost);

        var header = new ShellHubHeader
        {
            Title = "A Game With A Very Long Title",
            Tag = "PS4",
        };

        var scenes = new ShellSceneList
        {
            Scenes = new List<ShellScene>
            {
                new("Continue playing", Enumerable.Range(0, 4)
                    .Select(i => new ShellSceneItem($"Title {i + 1}")).ToList()),
                new("Recently added", Enumerable.Range(0, 4)
                    .Select(i => new ShellSceneItem($"Title {i + 5}")).ToList()),
            },
            Margin = new Thickness(ShellTileRow.ScaledExpMarginLeft, 180, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };

        var hub = new Panel
        {
            Margin = new Thickness(0, ShellHubMetrics.MarginTop, 0, 0),
        };
        hub.Children.Add(header);
        hub.Children.Add(scenes);

        var root = new Panel();
        root.Children.Add(new SharpEmu.GUI.SystemAssets.Shell.ShellBackground { IsMotionEnabled = true });
        root.Children.Add(page);
        root.Children.Add(hub);

        var transition = new ShellHubTransition { ManualClock = true };
        transition.Attach(page, strandHost);
        transition.Open();

        return new Scene(root, delta =>
        {
            transition.Advance(delta);
            row.Advance(delta);
        });
    }

    /// <summary>The function-control flyout at its own anchor, over the home.</summary>
    private static Scene FunctionPanel(Options options)
    {
        var home = Home(options);

        var panel = new ShellFunctionPanel
        {
            Header = "Power",
            Items = new List<ShellFunctionPanelItem>
            {
                new("Enter Rest Mode", "⏻"),
                new("Turn Off Console", "⏻"),
                new("Restart Console", "↻"),
                new("Sign Out", "→"),
            },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(
                ShellFunctionPanelMetrics.AnchorX,
                ShellFunctionPanelMetrics.AnchorY,
                0,
                0),
        };

        // The hub's utility strip, drawn at the left inset so its 56 on 48
        // rhythm can be checked against the nav band's above it.
        var utility = new ShellUtilityStrip
        {
            Items = new List<ShellUtilityItem>
            {
                new("Search", "⌕"),
                new("Filter", "≡"),
                new("Sort", "↕"),
                new("Options", "⋯"),
            },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(ShellTileRow.ScaledExpMarginLeft, 420, 0, 0),
        };

        var root = new Panel();
        root.Children.Add(home.Root);
        root.Children.Add(utility);
        root.Children.Add(panel);
        return new Scene(root, home.Advance);
    }

    private static ShellTileRow BuildRow(int count)
    {
        var row = new ShellTileRow
        {
            TileWidth = ShellTileRow.ScaledExperienceSize,
            TileHeight = ShellTileRow.ScaledExperienceSize,
            RestScale = ShellTileRow.ExperienceSize / ShellTileRow.ScaledExperienceSize,
            TileGap = ShellTileRow.DefaultItemMargin,
            FocusedMargin = ShellTileRow.DefaultFocusedMargin,
            TileCornerRadius = ShellTileRow.SwitcherStyles.FocusContainerBorderRadius,
            FocusAnchorX = ShellTileRow.ScaledExpMarginLeft,
            IsRegionFocused = true,
            // A headless run has no wall clock, so the row's own DispatcherTimer
            // would tick with a zero delta and nothing would ever move. The host
            // drives it instead.
            ManualClock = true,
        };

        var items = new List<ShellTile>();
        for (int i = 0; i < count; i++)
        {
            items.Add(i == 0
                ? new ShellTile("A Game With A Very Long Title That Has To Scroll To Be Read", "Bundled")
                : new ShellTile(
                    string.Format(CultureInfo.InvariantCulture, "Title {0}", i + 1),
                    "Publisher"));
        }

        row.Items = items;
        return row;
    }

    private static ShellTileRow BuildRow(IReadOnlyList<ShellTile> items)
    {
        var row = BuildRow(0);
        row.Items = items;
        return row;
    }

    private static Scene TileRow(Options options)
    {
        var row = BuildRow(options.Tiles);
        var host = new Panel { Height = ShellTileRow.ScaledExperienceSize };
        host.Children.Add(row);
        return new Scene(Wrap(host, top: 300), delta =>
        {
            row.Advance(delta);
            row.RefreshFocusRect();
            Dispatcher.UIThread.RunJobs();
            if (ShellFocusRing.For(row) is { } ring)
            {
                ring.ManualClock = true;
                ring.Advance(delta);
            }
        });
    }

    private static Scene Marquee(Options options)
    {
        var label = new ShellMarqueeText
        {
            Text = "A Game With A Very Long Title That Has To Scroll To Be Read",
            IsMarquee = true,
            FontSize = 26,
            Width = 420,
            Height = 40,
            ManualClock = true,
            Foreground = Brushes.White,
        };

        var host = new Panel { Height = 60 };
        host.Children.Add(label);
        return new Scene(Wrap(host, top: 400), label.Advance);
    }

    private static Scene Entrance(Options options)
    {
        var row = BuildRow(options.Tiles);
        var strandHost = new Panel { Height = ShellTileRow.ScaledExperienceSize };
        strandHost.Children.Add(row);

        var band = new ShellNavBand();
        band.SetClockText("21:45");

        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        Grid.SetRow(band, 0);
        page.Children.Add(band);
        Grid.SetRow(strandHost, 2);
        page.Children.Add(strandHost);

        var entrance = new ShellEntrance { ManualClock = true };
        entrance.Attach(strandHost, band, null);
        entrance.Begin(options.Tiles);

        return new Scene(page, delta =>
        {
            entrance.Advance(delta);
            row.Advance(delta);
        });
    }

    /// <summary>
    /// The settled home surface as the window composes it: the live background,
    /// then the 126 px nav band, then the 168 px switcher band under it. No
    /// entrance, so this is what the shell actually sits at.
    /// </summary>
    private static Scene Home(Options options)
    {
        var row = BuildRow(options.Tiles);
        var strandHost = new Panel { Height = ShellTileRow.ScaledExperienceSize };
        strandHost.Children.Add(row);

        var band = new ShellNavBand();
        band.SetClockText("21:45");

        var page = new Grid { RowDefinitions = new RowDefinitions("126,168,*") };
        Grid.SetRow(band, 0);
        page.Children.Add(band);
        Grid.SetRow(strandHost, 1);
        page.Children.Add(strandHost);

        var background = new SharpEmu.GUI.SystemAssets.Shell.ShellBackground
        {
            IsMotionEnabled = true,
            // HOME's resting particle state. The layer is gated on the global
            // background state, so leaving it at the default kept the particle
            // pass hidden and the capture showed only the plate.
            GlobalState = SharpEmu.GUI.SystemAssets.Shell.ShellGlobalBackgroundState.ParticleBottom,
        };
        var root = new Panel();
        root.Children.Add(background);
        root.Children.Add(page);

        // The particle layer normally advances on a 30 Hz DispatcherTimer, which
        // never ticks under headless capture. Pump it from the scene clock so a
        // captured frame shows the same layers the live shell draws - otherwise
        // captures silently omit the background and cannot be graded.
        return new Scene(root, elapsed =>
        {
            row.Advance(elapsed);
            background.NativeParticles.AdvanceFrameForCapture();
        });
    }

    /// <summary>
    /// The focus highlight travelling along the nav band's system icons.
    ///
    /// <para>The band is the one part of the home surface where the highlight
    /// itself moves rather than the content moving under it, so it is where the
    /// warp, the directional stretch and the dark first half of a move can
    /// actually be looked at.</para>
    /// </summary>
    private static Scene FocusNavBand(Options options)
    {
        var band = new ShellNavBand();
        band.SetClockText("21:45");
        band.FocusedRegion = ShellNavBand.SystemRegion;
        band.SetSelectedSystemIndex(0);

        var page = new Grid { RowDefinitions = new RowDefinitions("126,*") };
        Grid.SetRow(band, 0);
        page.Children.Add(band);

        var root = new Panel();
        root.Children.Add(new SharpEmu.GUI.SystemAssets.Shell.ShellBackground { IsMotionEnabled = true });
        root.Children.Add(page);

        var elapsed = TimeSpan.Zero;
        bool moved = false;

        return new Scene(root, delta =>
        {
            elapsed += delta;
            if (!moved && elapsed >= options.MoveAt)
            {
                moved = true;
                Console.WriteLine("focus moves at {0:0} ms", elapsed.TotalMilliseconds);
                band.SetSelectedSystemIndex(3);
            }

            Dispatcher.UIThread.RunJobs();

            if (ShellFocusRing.For(band) is { } ring)
            {
                ring.ManualClock = true;
                ring.Advance(delta);
            }
        });
    }

    /// <summary>
    /// The focus highlight travelling down a list.
    ///
    /// <para>This is the scene the travel actually shows in. The tile row anchors
    /// the focused tile at a fixed inset and slides the strand underneath it, so
    /// the highlight there never moves however correct its motion is — a list is
    /// the only place the warp, the directional stretch and the dark first half
    /// of a move are visible at all.</para>
    /// </summary>
    private static Scene FocusList(Options options)
    {
        var panel = new ShellFunctionPanel
        {
            Header = "Power",
            Items = new List<ShellFunctionPanelItem>
            {
                new("Enter Rest Mode", "⏻"),
                new("Turn Off Console", "⏻"),
                new("Restart Console", "↻"),
                new("Sign Out", "→"),
            },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(
                ShellFunctionPanelMetrics.AnchorX,
                ShellFunctionPanelMetrics.AnchorY,
                0,
                0),
        };

        var root = new Panel();
        root.Children.Add(new SharpEmu.GUI.SystemAssets.Shell.ShellBackground { IsMotionEnabled = true });
        root.Children.Add(panel);

        var elapsed = TimeSpan.Zero;
        bool moved = false;

        return new Scene(root, delta =>
        {
            elapsed += delta;
            if (!moved && elapsed >= options.MoveAt)
            {
                moved = true;
                Console.WriteLine("focus moves at {0:0} ms", elapsed.TotalMilliseconds);
                panel.SetSelectedIndex(3);
            }

            if (ShellFocusRing.For(panel) is { } ring)
            {
                ring.ManualClock = true;
                ring.Advance(delta);
            }
        });
    }

    /// <summary>
    /// The focus highlight travelling between tiles: the home surface, settled,
    /// with the selection moved once at <c>--move-at</c>.
    ///
    /// <para>The point is being able to look at the move rather than only at its
    /// endpoints. The band is dark for roughly the first half of a travel by
    /// design, so a capture taken only at rest cannot tell a correct
    /// implementation from one that drags a rectangle across the screen — the
    /// mid-move frames are the ones that show which it is.</para>
    /// </summary>
    private static Scene Focus(Options options)
    {
        var row = BuildRow(options.Tiles);
        var strandHost = new Panel { Height = ShellTileRow.ScaledExperienceSize };
        strandHost.Children.Add(row);

        var band = new ShellNavBand();
        band.SetClockText("21:45");

        var page = new Grid { RowDefinitions = new RowDefinitions("126,168,*") };
        Grid.SetRow(band, 0);
        page.Children.Add(band);
        Grid.SetRow(strandHost, 1);
        page.Children.Add(strandHost);

        var root = new Panel();
        // This target isolates focus motion. Parking the independent Plane2
        // clock avoids spending every headless pump rebuilding a full-screen
        // background while preserving the exact focused-card composite.
        root.Children.Add(new SharpEmu.GUI.SystemAssets.Shell.ShellBackground { IsMotionEnabled = false });
        root.Children.Add(page);

        var elapsed = TimeSpan.Zero;
        bool moved = false;

        return new Scene(root, delta =>
        {
            elapsed += delta;
            if (!moved && elapsed >= options.MoveAt)
            {
                moved = true;
                Console.WriteLine("focus moves at {0:0} ms", elapsed.TotalMilliseconds);
                row.SelectedIndex = 2;
            }

            row.Advance(delta);
            row.RefreshFocusRect();
            Dispatcher.UIThread.RunJobs();

            // The highlight lives on the scene's overlay layer, not inside the
            // row, so it has its own clock to advance.
            if (ShellFocusRing.For(row) is { } ring)
            {
                ring.ManualClock = true;
                ring.Advance(delta);
            }
        });
    }

    private static Control Wrap(Control inner, double top)
    {
        var grid = new Grid();
        inner.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        inner.Margin = new Thickness(0, top, 0, 0);
        grid.Children.Add(inner);
        return grid;
    }
}

internal sealed record Options(
    string Output,
    string Scene,
    int Frames,
    double StepMs,
    int Tiles,
    int Width,
    int Height,
    string? ArtA = null,
    string? ArtB = null,
    string? FirmwareRoot = null,
    string? HomeSource = null,
    TimeSpan MoveAt = default)
{
    public const string Usage =
        "usage: shell-shot --out <dir> [--scene tilerow|entrance|marquee|home|focus|focus-idle|list|navband|panel|hub|backdrop|wave-background|high-contrast-background|theme-one-background|native-background|native-background-bottom|firmware-home|settings|settings-detail|all-games|boot]\n" +
        "                  [--frames N] [--step MS] [--tiles N]\n" +
        "       backdrop:  --art-a <sce_sys dir> --art-b <sce_sys dir> [--move-at MS]\n" +
        "   firmware-home: --firmware-root <dump-root> [--home-source <NPXS40002.js>]";

    public static Options? Parse(string[] args)
    {
        string? output = null;
        string scene = "tilerow";
        int frames = 1;
        double step = 100;
        int tiles = 8;
        int width = 1920;
        int height = 1080;
        string? artA = null;
        string? artB = null;
        string? firmwareRoot = null;
        string? homeSource = null;
        double moveAtMs = 200;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: output = args[++i]; break;
                case "--scene" when i + 1 < args.Length: scene = args[++i]; break;
                case "--frames" when i + 1 < args.Length: frames = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--step" when i + 1 < args.Length: step = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--tiles" when i + 1 < args.Length: tiles = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--width" when i + 1 < args.Length: width = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--height" when i + 1 < args.Length: height = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--art-a" when i + 1 < args.Length: artA = args[++i]; break;
                case "--art-b" when i + 1 < args.Length: artB = args[++i]; break;
                case "--firmware-root" when i + 1 < args.Length: firmwareRoot = args[++i]; break;
                case "--home-source" when i + 1 < args.Length: homeSource = args[++i]; break;
                case "--move-at" when i + 1 < args.Length: moveAtMs = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                default: break;
            }
        }

        return output is null
            ? null
            : new Options(
                output, scene, frames, step, tiles, width, height,
                artA, artB, firmwareRoot, homeSource, TimeSpan.FromMilliseconds(moveAtMs));
    }
}
