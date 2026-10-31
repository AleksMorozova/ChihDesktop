using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChihDesktop;

public sealed class AnimationService : FrameworkElement
{
    private readonly Dictionary<CatState, ImageSource[]> clips = new();
    private CatState state;
    private CatDirection direction;
    private double elapsed;
    private int frame;

    public AnimationService()
    {
        foreach (var clip in ChihArtwork.Clips)
        {
            var frames = new ImageSource[clip.Count];
            for (int i = 0; i < frames.Length; i++)
            {
                // Decode embedded frames once; no disk access during animation or transitions.
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri($"pack://application:,,,/ChihDesktop;component/Assets/Sprites/{clip.Name}-{i:00}.png");
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                frames[i] = bitmap;
            }
            clips.Add(clip.State, frames);
        }
    }

    public void Update(CatState nextState, CatDirection nextDirection, double seconds)
    {
        bool changed = state != nextState || direction != nextDirection;
        if (state != nextState) elapsed = 0;
        state = nextState;
        direction = nextDirection;
        elapsed += seconds;
        int nextFrame = SelectFrame();
        if (changed || frame != nextFrame)
        {
            frame = nextFrame;
            InvalidateVisual();
        }
    }

    private int SelectFrame() => state switch
    {
        CatState.Walking => (int)(elapsed * ChihArtwork.FrameRate) % clips[state].Length,
        CatState.Idle => elapsed % 5 > 4.8 ? 1 : 0,
        CatState.Sleeping or CatState.Sitting => (int)(elapsed * 1.5) % clips[state].Length,
        CatState.Stretching => Math.Min((int)(elapsed * 2), clips[state].Length - 1),
        CatState.Reacting => (int)(elapsed * 4) % clips[state].Length,
        _ => 0
    };

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // Mirror the complete canvas, preserving its dimensions and ground baseline.
        if (direction == CatDirection.Left)
            dc.PushTransform(new MatrixTransform(-1, 0, 0, 1, ActualWidth, 0));
        dc.DrawImage(clips[state][frame], new Rect(0, 0, ActualWidth, ActualHeight));
        if (direction == CatDirection.Left) dc.Pop();
    }
}
