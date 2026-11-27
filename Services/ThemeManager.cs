using System;
using System.Windows;
using System.Windows.Media;

namespace SaveSpace.Services
{
    public static class ThemeManager
    {
        public static bool AnimationsEnabled { get; private set; } = true;

        public static void Apply(SettingsService settings)
        {
            var dark = settings.IsDark;
            var canvas = dark ? "#141518" : "#F2F0EC";
            var surface = dark ? "#1D1E22" : "#FFFFFF";
            var surfaceAlt = dark ? "#282A30" : "#EAE6DF";
            var surfaceRaised = dark ? "#34363D" : "#FFFFFF";
            var ink = dark ? "#F4F2F2" : "#1B1A19";
            var muted = dark ? "#C0C1C8" : "#6B6660";
            var faint = dark ? "#9B9CA4" : "#948D85";
            var line = dark ? "#383A41" : "#E1DBD2";
            var lineStrong = dark ? "#50525A" : "#CEC6BB";

            var accent = Parse(settings.Accent, dark ? "#F21D2F" : "#C8392B");
            var note = Parse(settings.ColorNote, dark ? "#F21D2F" : "#C8392B");
            var link = Parse(settings.ColorLink, dark ? "#3FB5A6" : "#147D74");
            var image = Parse(settings.ColorImage, dark ? "#E8A33D" : "#D98A2B");
            var video = Parse(settings.ColorVideo, dark ? "#C9A227" : "#A8730F");
            var document = Parse(settings.ColorDocument, dark ? "#A8A9B0" : "#6B6259");
            var folder = Parse(settings.ColorFolder, dark ? "#7FA268" : "#557A46");

            var surfaceColor = Parse(surface, "#17181C");
            var softAmount = dark ? 0.20 : 0.10;

            var res = Application.Current.Resources;
            res["UI.Compact"] = settings.Compact;
            res["Spacing.Notes"] = settings.Compact ? new Thickness(12, 10, 12, 12) : new Thickness(18, 14, 18, 16);
            Set(res, "Brush.Canvas", canvas);
            Set(res, "Brush.Surface", surface);
            Set(res, "Brush.SurfaceAlt", surfaceAlt);
            Set(res, "Brush.SurfaceRaised", surfaceRaised);
            Set(res, "Brush.Ink", ink);
            Set(res, "Brush.Muted", muted);
            Set(res, "Brush.Faint", faint);
            Set(res, "Brush.Line", line);
            Set(res, "Brush.LineStrong", lineStrong);
            res["Brush.Header"] = dark
                ? new LinearGradientBrush(Parse("#10090A", "#10090A"), Parse(surface, "#191A1D"), 0)
                : NewBrush(surface);
            Set(res, "Brush.OnAccent", "#FFF8F8");
            Set(res, "Brush.Accent", accent);
            Set(res, "Brush.AccentDeep", Parse(dark ? "#8E1722" : "#8E1722", "#8E1722"));
            Set(res, "Brush.AccentSoft", Blend(accent, surfaceColor, softAmount));
            Set(res, "Brush.AccentBorder", Blend(accent, surfaceColor, 0.34));
            Set(res, "Brush.NavActive", dark ? accent : Parse(ink, "#1B1A19"));
            Set(res, "Brush.NavActiveInk", dark ? "#FFFFFF" : surface);
            Set(res, "Brush.Amber", image);
            Set(res, "Brush.AmberSoft", Blend(image, surfaceColor, softAmount));
            Set(res, "Brush.Teal", link);
            Set(res, "Brush.TealSoft", Blend(link, surfaceColor, softAmount));
            Set(res, "Brush.DarkYellow", video);
            Set(res, "Brush.DarkYellowSoft", Blend(video, surfaceColor, softAmount));
            Set(res, "Brush.Sage", folder);
            Set(res, "Brush.SageSoft", Blend(folder, surfaceColor, softAmount));
            Set(res, "Brush.Note", note);
            Set(res, "Brush.NoteSoft", Blend(note, surfaceColor, softAmount));
            Set(res, "Brush.Link", link);
            Set(res, "Brush.LinkSoft", Blend(link, surfaceColor, softAmount));
            Set(res, "Brush.Image", image);
            Set(res, "Brush.ImageSoft", Blend(image, surfaceColor, softAmount));
            Set(res, "Brush.Video", video);
            Set(res, "Brush.VideoSoft", Blend(video, surfaceColor, softAmount));
            Set(res, "Brush.Document", document);
            Set(res, "Brush.DocumentSoft", Blend(document, surfaceColor, softAmount));
            Set(res, "Brush.Folder", folder);
            Set(res, "Brush.FolderSoft", Blend(folder, surfaceColor, softAmount));
            Set(res, "Brush.Danger", dark ? "#F06A5C" : "#B3261E");
            Set(res, "Brush.DangerSoft", Blend(Parse(dark ? "#F06A5C" : "#B3261E", "#B3261E"), surfaceColor, softAmount));
            Set(res, "Brush.Success", dark ? "#6FBF8B" : "#2F7D4F");
            Set(res, "Brush.SuccessSoft", Blend(Parse(dark ? "#6FBF8B" : "#2F7D4F", "#2F7D4F"), surfaceColor, softAmount));
            Set(res, "Brush.Overlay", dark ? "#B3000000" : "#66000000");

            res["Brush.HeroGradient"] = dark
                ? new LinearGradientBrush(Parse("#5A0D15", "#5A0D15"), Parse("#111112", "#111112"), 18)
                : new LinearGradientBrush(Parse("#8E1722", "#8E1722"), Parse("#3A0B10", "#3A0B10"), 18);
            res["Brush.HeroInk"] = NewBrush("#FFF7F7");
            res["Brush.HeroMuted"] = NewBrush(dark ? "#E8B8BC" : "#F2C9CB");
            res["Brush.HeroCard"] = NewBrush("#1FFFFFFF");

            var scale = settings.FontScale;
            res["Font.Display"] = 36.0 * scale;
            res["Font.H1"] = 26.0 * scale;
            res["Font.H2"] = 20.0 * scale;
            res["Font.H3"] = 15.5 * scale;
            res["Font.Body"] = 13.5 * scale;
            res["Font.Small"] = 12.0 * scale;
            res["Font.Tiny"] = 11.0 * scale;
            res["Font.Micro"] = 10.0 * scale;
            res["Font.Metric"] = 30.0 * scale;
            res["Font.LineDisplay"] = 42.0 * scale;
            res["Font.LineBody"] = 21.0 * scale;

            var radius = settings.Radius;
            res["Radius.S"] = new CornerRadius(2.0 + radius * 0.18);
            res["Radius.M"] = new CornerRadius(3.0 + radius * 0.24);
            res["Radius.L"] = new CornerRadius(4.0 + radius * 0.32);
            res["Radius.XL"] = new CornerRadius(5.0 + radius * 0.38);
            res["Radius.Pill"] = new CornerRadius(7.0 + radius * 0.30);

            res["Font.BodyFamily"] = new FontFamily("Segoe UI Variable Text, Segoe UI");
            res["Font.DisplayFamily"] = new FontFamily("Bahnschrift SemiBold, Segoe UI Variable Display, Segoe UI Semibold, Segoe UI");
            res["Font.MonoFamily"] = new FontFamily("Cascadia Mono, Consolas");

            res["Shadow.Soft"] = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 7,
                Direction = 270,
                Opacity = dark ? 0.30 : 0.09,
                Color = Colors.Black
            };
            res["Shadow.Hover"] = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 36,
                ShadowDepth = 11,
                Direction = 270,
                Opacity = dark ? 0.44 : 0.15,
                Color = Colors.Black
            };

            try { ApplyPreset(res, settings, dark, accent); }
            catch (Exception ex) { StartupLog.Fail("Lay-out toepassen: " + ex); AppPaths.Log("Lay-out toepassen mislukt: " + ex.Message); }
            if (settings.HighContrast) ApplyHighContrast(res, dark);
            ApplyHomeBackdrop(res, settings.LayoutPreset, dark);
            if (settings.HighContrast)
            {
                res["Brush.HomeBackground"] = res["Brush.Canvas"];
                res["Brush.HomeGlow"] = new SolidColorBrush(Colors.Transparent);
                res["Brush.HomeWave"] = new SolidColorBrush(Colors.Transparent);
                res["Home.WaveOpacity"] = 0.0;
            }
            else ApplyCustomHomeBackground(res, settings, dark);

            AnimationsEnabled = settings.Animations;
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.FontFamily = (FontFamily)res["Font.BodyFamily"];
                Application.Current.MainWindow.FontSize = (double)res["Font.Body"];
                Application.Current.MainWindow.Background = (Brush)res["Brush.Canvas"];
            }
        }

        /// <summary>Elke lay-out heeft een eigen palet, vlakbehandeling en lettertype.</summary>
        private sealed record Palette(
            Brush Canvas, Brush Surface, Brush SurfaceAlt, Brush SurfaceRaised,
            Brush Line, Brush LineStrong, Brush Header,
            string Accent, string BodyFont, string DisplayFont,
            double ShadowOpacity, double ShadowBlur);

        private const string DefaultDarkAccent = "#F21D2F";
        private const string DefaultLightAccent = "#C8392B";

        private static void ApplyPreset(ResourceDictionary res, SettingsService settings, bool dark, Color userAccent)
        {
            var (lineHeight, reading, hoverBlur) = settings.LayoutPreset switch
            {
                "claude" => (28.0, 820.0, 40.0),
                "apple" => (26.0, 760.0, 44.0),
                "focus" => (32.0, 680.0, 0.0),
                "library" => (22.0, 0.0, 22.0),
                _ => (25.0, 0.0, 36.0)
            };
            var (pageSlide, pageDuration, showAttachments) = settings.LayoutPreset switch
            {
                "claude" => (14.0, 300.0, 1.0),
                "apple" => (22.0, 420.0, 1.0),
                "focus" => (0.0, 160.0, 0.0),
                "library" => (6.0, 190.0, 1.0),
                _ => (12.0, 240.0, 1.0)
            };
            res["Layout.PageSlide"] = pageSlide;
            res["Layout.PageDuration"] = pageDuration;
            res["Layout.ShowAttachments"] = showAttachments;
            res["Layout.RowPadding"] = settings.LayoutPreset switch
            {
                "claude" => new Thickness(12, 10, 12, 10),
                "apple" => new Thickness(12, 11, 12, 11),
                "focus" => new Thickness(10, 6, 10, 6),
                "library" => new Thickness(10, 5, 10, 5),
                _ => new Thickness(10, 8, 10, 8)
            };
            // De merkbalk houdt in alle presets een zachte rode lichtboog; de inhoud blijft per stijl anders.
            res["Header.RedBeamOpacity"] = settings.LayoutPreset == "focus" ? 0.78 : 1.0;
            res["Layout.LineHeight"] = lineHeight;
            res["Layout.ReadingWidth"] = reading;
            res["Shadow.Hover"] = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = hoverBlur,
                ShadowDepth = hoverBlur * 0.3,
                Direction = 270,
                Opacity = hoverBlur == 0 ? 0 : (dark ? 0.42 : 0.14),
                Color = Colors.Black
            };

            var p = PaletteFor(settings.LayoutPreset, dark);
            if (p == null) return;

            res["Brush.Canvas"] = p.Canvas;
            res["Brush.Surface"] = p.Surface;
            res["Brush.SurfaceAlt"] = p.SurfaceAlt;
            res["Brush.SurfaceRaised"] = p.SurfaceRaised;
            res["Brush.Line"] = p.Line;
            res["Brush.LineStrong"] = p.LineStrong;
            res["Brush.Header"] = p.Header;
            res["Font.BodyFamily"] = new FontFamily(p.BodyFont);
            res["Font.DisplayFamily"] = new FontFamily(p.DisplayFont);
            res["Shadow.Soft"] = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = p.ShadowBlur,
                ShadowDepth = p.ShadowBlur * 0.27,
                Direction = 270,
                Opacity = p.ShadowOpacity,
                Color = Colors.Black
            };

            // Een eigen accentkleur van de gebruiker blijft altijd leidend.
            var isDefault = ToHex(userAccent).Equals(dark ? DefaultDarkAccent : DefaultLightAccent, StringComparison.OrdinalIgnoreCase);
            if (isDefault)
            {
                var accent = Parse(p.Accent, dark ? DefaultDarkAccent : DefaultLightAccent);
                var surfaceColor = Parse(dark ? "#1D1E22" : "#FFFFFF", "#17181C");
                res["Brush.Accent"] = new SolidColorBrush(accent);
                res["Brush.AccentSoft"] = new SolidColorBrush(Blend(accent, surfaceColor, dark ? 0.20 : 0.10));
                res["Brush.AccentBorder"] = new SolidColorBrush(Blend(accent, surfaceColor, 0.34));
                res["Brush.NavActive"] = new SolidColorBrush(accent);
            }

            // Houd standaardcategorieën binnen de gekozen stijl; expliciet aangepaste kleuren blijven behouden.
            if (settings.LayoutPreset != "claude") ApplyCategoryPalette(res, settings, dark, settings.LayoutPreset);
        }

        private static void ApplyCategoryPalette(ResourceDictionary res, SettingsService settings, bool dark, string preset)
        {
            var colors = preset switch
            {
                "apple" => (Note: "#78B8FF", Link: "#49B7A5", Image: "#E6A94D", Video: "#A08BE4", Document: "#6788B6", Folder: "#51A998"),
                "focus" => (Note: "#B7A1F7", Link: "#8BA7CE", Image: "#B49ACF", Video: "#817F9D", Document: "#9698A5", Folder: "#9889BD"),
                _ => (Note: "#EE2737", Link: "#E45B68", Image: "#D98166", Video: "#C54A56", Document: "#B9AEB0", Folder: "#A84A53")
            };
            var defaults = dark
                ? (Note: "#F21D2F", Link: "#3FB5A6", Image: "#E8A33D", Video: "#C9A227", Document: "#A8A9B0", Folder: "#7FA268")
                : (Note: "#C8392B", Link: "#147D74", Image: "#D98A2B", Video: "#A8730F", Document: "#6B6259", Folder: "#557A46");
            var surface = Parse(dark ? "#1D1E22" : "#FFFFFF", "#17181C");
            var amount = dark ? 0.20 : 0.10;

            Apply("Note", settings.ColorNote, defaults.Note, colors.Note);
            Apply("Link", settings.ColorLink, defaults.Link, colors.Link);
            Apply("Image", settings.ColorImage, defaults.Image, colors.Image);
            Apply("Video", settings.ColorVideo, defaults.Video, colors.Video);
            Apply("Document", settings.ColorDocument, defaults.Document, colors.Document);
            Apply("Folder", settings.ColorFolder, defaults.Folder, colors.Folder);

            void Apply(string name, string configured, string defaultColor, string presetColor)
            {
                var chosen = ToHex(Parse(configured, defaultColor)).Equals(defaultColor, StringComparison.OrdinalIgnoreCase)
                    ? Parse(presetColor, defaultColor)
                    : Parse(configured, defaultColor);
                res[$"Brush.{name}"] = new SolidColorBrush(chosen);
                res[$"Brush.{name}Soft"] = new SolidColorBrush(Blend(chosen, surface, amount));
                switch (name)
                {
                    case "Link": res["Brush.Teal"] = new SolidColorBrush(chosen); res["Brush.TealSoft"] = new SolidColorBrush(Blend(chosen, surface, amount)); break;
                    case "Image": res["Brush.Amber"] = new SolidColorBrush(chosen); res["Brush.AmberSoft"] = new SolidColorBrush(Blend(chosen, surface, amount)); break;
                    case "Video": res["Brush.DarkYellow"] = new SolidColorBrush(chosen); res["Brush.DarkYellowSoft"] = new SolidColorBrush(Blend(chosen, surface, amount)); break;
                    case "Folder": res["Brush.Sage"] = new SolidColorBrush(chosen); res["Brush.SageSoft"] = new SolidColorBrush(Blend(chosen, surface, amount)); break;
                }
            }
        }

        private static Palette? PaletteFor(string preset, bool dark) => preset switch
        {
            "claude" => dark
                ? new Palette(Solid("#1C1A17"), Solid("#252320"), Solid("#302C27"), Solid("#2E2A25"),
                    Solid("#3B352E"), Solid("#5A5249"), Solid("#22201C"),
                    "#D97757", "Segoe UI Variable Text, Segoe UI", "Georgia, Segoe UI Variable Display, Segoe UI", 0.30, 26)
                : new Palette(Solid("#F5F0E8"), Solid("#FFFCF7"), Solid("#EDE6DB"), Solid("#FFFFFF"),
                    Solid("#DED5C7"), Solid("#C4B8A6"), Solid("#FBF8F3"),
                    "#C2613E", "Segoe UI Variable Text, Segoe UI", "Georgia, Segoe UI Variable Display, Segoe UI", 0.08, 24),

            // Liquid Glass-geïnspireerd: kleur blijft in de achtergrond, bediening ligt op lichte glaslagen.
            "apple" => dark
                ? new Palette(Gradient(("#10172A", 0), ("#282746", 0.48), ("#12323A", 1)),
                    Glass(0x43, 0x1D, 235, 242, 255), Glass(0x34, 0x18, 224, 236, 255), Glass(0x58, 0x28, 245, 249, 255),
                    Solid("#596E8AA8"), Solid("#8095B4D0"), Glass(0x4A, 0x1C, 235, 242, 255),
                    "#78B8FF", "Segoe UI Variable Text, Segoe UI", "Segoe UI Variable Display, Segoe UI Semibold, Segoe UI", 0.24, 24)
                : new Palette(Gradient(("#DDE9FA", 0), ("#EFE7F5", 0.48), ("#DBF0ED", 1)),
                    Glass(0xC9, 0x88, 255, 255, 255), Glass(0xA8, 0x6A, 249, 251, 255), Glass(0xE8, 0xB8, 255, 255, 255),
                    Solid("#4C7188A4"), Solid("#796782A0"), Glass(0xD9, 0x96, 255, 255, 255),
                    "#246BD1", "Segoe UI Variable Text, Segoe UI", "Segoe UI Variable Display, Segoe UI Semibold, Segoe UI", 0.12, 22),

            // Focus: weinig chroom, rustig nachtblauw met een ingetogen lavendelkleurige focusaccent.
            "focus" => dark
                ? new Palette(Solid("#101116"), Solid("#15171E"), Solid("#1D2029"), Solid("#242834"),
                    Solid("#2B2E39"), Solid("#414654"), Solid("#101116"),
                    "#B7A1F7", "Georgia, Segoe UI", "Georgia, Segoe UI", 0.08, 8)
                : new Palette(Solid("#F6F5FA"), Solid("#FFFFFF"), Solid("#EEECF5"), Solid("#FFFFFF"),
                    Solid("#E3E0ED"), Solid("#C8C2D8"), Solid("#F6F5FA"),
                    "#7357B8", "Georgia, Segoe UI", "Georgia, Segoe UI", 0.06, 8),

            // Bibliotheek: bijna zwart, nette grijstinten en de smalle rode accentkleur uit de referentie.
            "library" => dark
                ? new Palette(Solid("#0D0D0F"), Solid("#151517"), Solid("#1C1C1F"), Solid("#242427"),
                    Solid("#303034"), Solid("#48484E"), Gradient(("#080808", 0), ("#17090B", 0.56), ("#0A090A", 1)),
                    "#EE2737", "Segoe UI Variable Text, Segoe UI", "Segoe UI Variable Display, Segoe UI Semibold, Segoe UI", 0.25, 16)
                : new Palette(Solid("#F2F2F3"), Solid("#FFFFFF"), Solid("#E8E8EA"), Solid("#FFFFFF"),
                    Solid("#D9D9DC"), Solid("#BDBDC2"), Solid("#F5F5F6"),
                    "#D91F30", "Segoe UI Variable Text, Segoe UI", "Segoe UI Variable Display, Segoe UI Semibold, Segoe UI", 0.08, 14),

            _ => null
        };

        private static void ApplyHomeBackdrop(ResourceDictionary res, string preset, bool dark)
        {
            var canvas = (Brush)res["Brush.Canvas"];
            var background = canvas;
            var glow = Colors.Transparent;
            var wave = Colors.Transparent;
            var waveOpacity = 0.0;

            if (preset == "library" && dark)
            {
                background = VerticalGradient(("#650000", 0), ("#2A0000", 0.055), ("#160204", 0.12),
                    ("#0C0809", 0.25), ("#090909", 0.42), ("#080808", 0.62), ("#080808", 1));
                glow = Color.FromArgb(46, 242, 29, 47);
                wave = Color.FromArgb(182, 242, 29, 47);
                waveOpacity = 0.46;
            }
            else if (preset == "library")
            {
                background = VerticalGradient(("#F8EBED", 0), ("#F3F1F1", 0.32), ("#F2F2F3", 1));
                glow = Color.FromArgb(28, 217, 31, 48);
                wave = Color.FromArgb(95, 217, 31, 48);
                waveOpacity = 0.18;
            }
            else if (preset == "apple")
            {
                glow = dark ? Color.FromArgb(48, 100, 153, 255) : Color.FromArgb(36, 105, 149, 231);
                wave = dark ? Color.FromArgb(150, 121, 175, 255) : Color.FromArgb(95, 74, 119, 211);
                waveOpacity = dark ? 0.20 : 0.12;
            }
            else if (preset == "focus")
            {
                background = dark
                    ? VerticalGradient(("#211A2A", 0), ("#17151D", 0.28), ("#101116", 0.72), ("#101116", 1))
                    : VerticalGradient(("#F1EDF7", 0), ("#F6F5FA", 0.42), ("#F6F5FA", 1));
                glow = dark ? Color.FromArgb(30, 183, 161, 247) : Color.FromArgb(24, 115, 87, 184);
                wave = dark ? Color.FromArgb(100, 183, 161, 247) : Color.FromArgb(70, 115, 87, 184);
                waveOpacity = 0.14;
            }

            res["Brush.HomeBackground"] = background;
            res["Brush.HomeGlow"] = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
                GradientStops = new GradientStopCollection
                {
                    new(glow, 0),
                    new(Color.FromArgb(0, glow.R, glow.G, glow.B), 1)
                }
            };
            res["Brush.HomeWave"] = new SolidColorBrush(wave);
            res["Home.WaveOpacity"] = waveOpacity;
        }

        private static void ApplyCustomHomeBackground(ResourceDictionary res, SettingsService settings, bool dark)
        {
            if (string.IsNullOrWhiteSpace(settings.HomeBackgroundColor)) return;
            var canvasColor = GetRepresentativeColor((Brush)res["Brush.Canvas"], dark ? "#101116" : "#F7F7F8");
            var tint = Parse(settings.HomeBackgroundColor, dark ? "#650000" : "#F8EBED");
            if (!dark)
            {
                res["Brush.HomeBackground"] = VerticalGradient((tint.ToString(), 0), (canvasColor.ToString(), 0.30), (canvasColor.ToString(), 1));
            }
            else
            {
                var mid = Blend(tint, canvasColor, 0.62);
                res["Brush.HomeBackground"] = VerticalGradient((tint.ToString(), 0), (mid.ToString(), 0.16), (canvasColor.ToString(), 0.58), (canvasColor.ToString(), 1));
            }
            res["Brush.HomeGlow"] = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5,
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb(dark ? (byte)48 : (byte)28, tint.R, tint.G, tint.B), 0),
                    new(Color.FromArgb(0, tint.R, tint.G, tint.B), 1)
                }
            };
        }

        private static Color GetRepresentativeColor(Brush brush, string fallback)
        {
            if (brush is SolidColorBrush solid) return solid.Color;
            if (brush is GradientBrush gradient && gradient.GradientStops.Count > 0)
                return gradient.GradientStops.OrderBy(s => Math.Abs(s.Offset - 0.65)).First().Color;
            return Parse(fallback, "#101116");
        }

        private static void ApplyHighContrast(ResourceDictionary res, bool dark)
        {
            var canvas = dark ? "#000000" : "#FFFFFF";
            var surface = dark ? "#000000" : "#FFFFFF";
            var alt = dark ? "#111111" : "#F1F1F1";
            var text = dark ? "#FFFFFF" : "#000000";
            var accent = dark ? "#FFFF00" : "#8B0000";
            Set(res, "Brush.Canvas", canvas);
            Set(res, "Brush.Surface", surface);
            Set(res, "Brush.SurfaceAlt", alt);
            Set(res, "Brush.SurfaceRaised", alt);
            Set(res, "Brush.Ink", text);
            Set(res, "Brush.Muted", text);
            Set(res, "Brush.Faint", dark ? "#E6E6E6" : "#222222");
            Set(res, "Brush.Line", dark ? "#FFFFFF" : "#000000");
            Set(res, "Brush.LineStrong", dark ? "#FFFFFF" : "#000000");
            Set(res, "Brush.Accent", accent);
            Set(res, "Brush.OnAccent", dark ? "#000000" : "#FFFFFF");
            Set(res, "Brush.AccentSoft", dark ? "#252500" : "#F6EAEA");
            Set(res, "Brush.AccentBorder", accent);
            res["Brush.Header"] = NewBrush(canvas);
        }

        private static LinearGradientBrush VerticalGradient(params (string Hex, double Offset)[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            foreach (var (hex, offset) in stops)
                brush.GradientStops.Add(new GradientStop(Parse(hex, "#080808"), offset));
            return brush;
        }

        private static SolidColorBrush Solid(string hex) => NewBrush(hex);

        /// <summary>Leest een lay-outwaarde uit de resources, met een vaste terugvaloptie.</summary>
        public static double LayoutValue(string key, double fallback) =>
            Application.Current?.TryFindResource(key) is double value ? value : fallback;

        /// <summary>Vlak met verloop van boven naar onder, gebruikt voor glanzende glasvlakken.</summary>
        private static LinearGradientBrush Glass(byte alphaTop, byte alphaBottom, byte r, byte g, byte b) =>
            new(Color.FromArgb(alphaTop, r, g, b), Color.FromArgb(alphaBottom, r, g, b), 90);

        /// <summary>Diagonaal verloop over de hele achtergrond; de kleuren geven het glas iets om doorheen te tonen.</summary>
        private static LinearGradientBrush Gradient(params (string Hex, double Offset)[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            foreach (var (hex, offset) in stops)
                brush.GradientStops.Add(new GradientStop(Parse(hex, "#000000"), offset));
            return brush;
        }

        private static void Set(ResourceDictionary res, string key, string hex)
        {
            res[key] = NewBrush(hex);
        }

        private static void Set(ResourceDictionary res, string key, Color color)
        {
            res[key] = new SolidColorBrush(color);
        }

        public static SolidColorBrush NewBrush(string hex) => new(Parse(hex, "#000000"));

        public static Color Parse(string hex, string fallback)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString(fallback);
            }
        }

        public static Color Darken(Color c, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromRgb((byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));
        }

        public static Color Lighten(Color c, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromRgb((byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));
        }

        public static Color Blend(Color fg, Color bg, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromRgb(
                (byte)(bg.R + (fg.R - bg.R) * amount),
                (byte)(bg.G + (fg.G - bg.G) * amount),
                (byte)(bg.B + (fg.B - bg.B) * amount));
        }

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static Brush BrushOf(string key) => (Brush)Application.Current.Resources[key];
    }
}
