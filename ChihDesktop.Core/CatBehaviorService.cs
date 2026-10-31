namespace ChihDesktop;

public enum CatState { Idle, Walking, Sitting, Sleeping, Stretching, Reacting }
public enum CatDirection { Left, Right }

public sealed class CatBehaviorService
{
    private readonly Random random = new();
    private double remaining = 2;
    private bool forcedSleep;
    public CatState State { get; private set; } = CatState.Idle;
    public CatDirection Direction { get; private set; } = CatDirection.Right;
    public bool IsPaused { get; set; }

    public void Update(double seconds)
    {
        if (IsPaused || forcedSleep) return;
        remaining -= seconds;
        if (remaining > 0) return;
        if (State == CatState.Walking) { SetState(CatState.Idle, 1, 3); return; }
        if (State == CatState.Sleeping) { SetState(CatState.Stretching, 1.5, 2.5); return; }
        // Weights: walking 40%, idle 20%, sitting 25%, sleeping 15%.
        int choice = random.Next(100);
        if (choice < 40)
        {
            Direction = random.Next(2) == 0 ? CatDirection.Left : CatDirection.Right;
            SetState(CatState.Walking, 3, 9);
        }
        else if (choice < 60) SetState(CatState.Idle, 2, 5);
        else if (choice < 85) SetState(CatState.Sitting, 4, 9);
        else SetState(CatState.Sleeping, 10, 22);
    }

    public void TurnAround() => Direction = Direction == CatDirection.Left ? CatDirection.Right : CatDirection.Left;
    public void React() { forcedSleep = false; SetState(CatState.Reacting, .8, 1.2); }
    public void Sleep() { forcedSleep = true; State = CatState.Sleeping; }
    public void WakeUp() { forcedSleep = false; SetState(CatState.Stretching, 1.5, 2); }
    private void SetState(CatState state, double min, double max)
    { State = state; remaining = min + random.NextDouble() * (max - min); }
}

