using System.Windows;
using System.Windows.Media;

namespace ChihDesktop;

// Original vector fallback guided by Chih's photographs; no atlas pixels are used.
// Normalized 160x130 drawing canvas, paw baseline y=122; exported at 2x resolution.
public static class ChihArtwork
{
    public const int PixelWidth = 320, PixelHeight = 260;
    public const double FrameRate = 8;
    public static readonly (string Name, CatState State, int Count)[] Clips =
    [
        ("Idle", CatState.Idle, 2), ("WalkRight", CatState.Walking, 4),
        ("Sit", CatState.Sitting, 2), ("Sleep", CatState.Sleeping, 2),
        ("Stretch", CatState.Stretching, 3), ("React", CatState.Reacting, 2)
    ];
    private static readonly Brush Black = ColorBrush("#202127"), White = ColorBrush("#F5F2E9");
    private static readonly Brush Shade = ColorBrush("#DDDCD6"), Pink = ColorBrush("#D998A1");
    private static readonly Brush Ear = ColorBrush("#9B747F"), Eye = ColorBrush("#C1C36B");
    private static readonly Pen Edge = new(ColorBrush("#55565C"), .8);
    private static readonly Pen Fine = new(ColorBrush("#66656A"), .65);
    private static readonly Pen Whisker = new(ColorBrush("#DDDCD6"), .55);

    public static DrawingGroup Draw(CatState state, int frame)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            bool sit = state is CatState.Sitting or CatState.Idle or CatState.Reacting;
            bool stretch = state == CatState.Stretching;
            double breath = frame % 2 == 0 ? 0 : .8;
            if (state == CatState.Sleeping)
            {
                Shape(dc, Black, "M 23,111 C 10,79 40,70 70,80 C 86,80 100,91 107,105 L 112,119 L 30,121 Z");
                dc.DrawEllipse(White, null, new Point(72, 109 - breath), 31, 11);
                Tail(dc, "M 65,114 C 38,118 8,115 17,103 C 22,96 31,104 34,108", 12);
                Paw(dc, 82, 118, 13);
                Head(dc, 109, 90 - breath, true, false, false);
            }
            else if (sit)
            {
                Tail(dc, "M 50,113 C 14,124 10,105 24,101 C 35,98 38,113 48,112", 11);
                Shape(dc, Black, $"M 43,114 C 29,92 40,60 65,{53 - breath} C 86,43 105,66 104,89 L 119,117 Q 84,128 43,114 Z");
                Shape(dc, White, "M 73,64 L 96,60 L 106,77 L 100,83 L 104,88 L 97,90 L 102,96 L 96,101 L 99,114 Q 85,124 72,114 L 70,96 L 65,91 L 71,88 L 66,83 Z");
                Leg(dc, 78, 93, 76, 116, false);
                Leg(dc, 100, 91, 103, 116, false);
                Paw(dc, 55, 117, 14);
                Paw(dc, 76, 118, 9);
                Paw(dc, 104, 118, 9);
                bool react = state == CatState.Reacting;
                Head(dc, 92, (react ? 46 : 51) - breath, false, state == CatState.Idle && frame == 1, react);
                if (react && frame == 1)
                {
                    Leg(dc, 108, 91, 130, 81, false);
                    dc.DrawEllipse(White, Edge, new Point(132, 77), 7, 10);
                    dc.DrawEllipse(Pink, null, new Point(133, 77), 2.5, 3);
                }
            }
            else
            {
                double stride = state == CatState.Walking ? Math.Sin(frame * Math.PI / 2) * 8 : 0;
                double farLift = state == CatState.Walking ? Math.Max(0, Math.Cos(frame * Math.PI / 2)) * 3 : 0;
                double nearLift = state == CatState.Walking ? Math.Max(0, -Math.Cos(frame * Math.PI / 2)) * 3 : 0;
                double bodyY = stretch ? 91 - frame * 2 : 86 - (frame % 2) * 1.2;
                Tail(dc, stretch ? "M 42,91 C 23,73 24,39 39,47" : "M 40,88 C 17,76 20,43 31,42 C 38,41 34,54 32,60", 10);
                Leg(dc, 53, 95, 53 - stride, 116 - farLift, true);
                Leg(dc, 104, 96, 104 + stride, 116 - farLift, true);
                dc.DrawEllipse(Black, Edge, new Point(73, bodyY), stretch ? 43 : 40, stretch ? 20 : 25);
                Shape(dc, White, $"M 38,{bodyY + 6} Q 67,{bodyY + 15} 98,{bodyY + 3} L 111,{bodyY + 16} Q 75,120 41,109 Z");
                Leg(dc, 45, 96, stretch ? 34 : 45 + stride, 116 - nearLift, false);
                Leg(dc, 107, 91, stretch ? 127 : 107 - stride, 116 - nearLift, false);
                Shape(dc, White, "M 99,70 L 117,69 L 121,87 L 115,93 L 115,104 L 104,108 L 98,93 L 94,90 Z");
                Head(dc, stretch ? 121 : 117, stretch ? 88 - frame * 2 : 66, false, false, false);
            }
        }
        drawing.Freeze();
        return drawing;
    }

    private static void Head(DrawingContext dc, double x, double y, bool sleeping, bool blink, bool react)
    {
        dc.PushTransform(new TranslateTransform(x - 110, y - 60));
        Shape(dc, Black, "M 88,49 L 86,25 Q 96,27 103,40 L 119,39 Q 128,26 134,26 L 133,53 Z");
        Shape(dc, Ear, "M 90,31 L 92,45 L 100,41 Z M 129,32 L 123,42 L 130,46 Z");
        Shape(dc, Black, "M 88,46 Q 110,33 132,47 Q 140,59 135,69 L 140,73 L 134,75 L 137,79 L 130,80 Q 111,93 91,79 L 85,80 L 88,75 L 82,73 L 86,69 Q 81,58 88,46 Z");
        // Narrow forehead blaze widens into the muzzle; black surrounds both eyes.
        Shape(dc, White, "M 110,43 L 113,54 L 112,63 L 120,68 Q 130,64 133,70 L 135,76 Q 112,92 89,76 L 89,70 Q 94,65 103,68 L 108,61 L 109,52 Z");
        if (sleeping || blink)
            dc.DrawGeometry(null, Fine, Geometry.Parse("M 93,61 Q 99,65 104,61 M 118,61 Q 124,65 129,60"));
        else { Eyes(dc, 99, 59, react); Eyes(dc, 124, 59, react); }
        Shape(dc, Pink, "M 106,71 Q 111,69 116,71 L 111,76 Z");
        dc.DrawGeometry(null, Fine, Geometry.Parse("M 111,76 L 111,79 M 104,79 Q 108,82 111,79 Q 115,82 119,78"));
        dc.DrawGeometry(null, Whisker, Geometry.Parse("M 101,73 L 78,68 M 101,76 L 76,77 M 121,73 L 143,68 M 122,76 L 146,77"));
        if (react) dc.DrawEllipse(Pink, Fine, new Point(112, 82), 3, 2);
        dc.Pop();
    }
    private static void Eyes(DrawingContext dc, double x, double y, bool wide)
    {
        dc.DrawEllipse(Eye, Fine, new Point(x, y), 5.3, wide ? 6.5 : 4.8);
        dc.DrawEllipse(Black, null, new Point(x + .6, y), wide ? 2.8 : 1.7, 4.3);
        dc.DrawEllipse(White, null, new Point(x - 1.5, y - 2), 1.1, 1.1);
    }
    private static void Leg(DrawingContext dc, double x, double y, double footX, double footY, bool far)
    {
        dc.DrawLine(new Pen(far ? Shade : White, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(x, y), new Point(footX, footY));
        Paw(dc, footX + 2, footY + 2, 8, far);
    }
    private static void Paw(DrawingContext dc, double x, double y, double width, bool far = false)
    {
        dc.DrawEllipse(far ? Shade : White, Edge, new Point(x, y), width, 4);
        dc.DrawLine(Fine, new Point(x + width * .3, y), new Point(x + width * .3, y + 2));
    }
    private static void Tail(DrawingContext dc, string path, double width)
    {
        var geometry = Geometry.Parse(path);
        dc.DrawGeometry(null, new Pen(Edge.Brush, width + 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        dc.DrawGeometry(null, new Pen(Black, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
    }
    private static void Shape(DrawingContext dc, Brush fill, string path) => dc.DrawGeometry(fill, Edge, Geometry.Parse(path));
    private static Brush ColorBrush(string hex)
    { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; brush.Freeze(); return brush; }
}
