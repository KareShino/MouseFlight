namespace MFlight.Demo
{
    public interface IPlanePilot
    {
        void Initialize(Plane plane);
        void TickPilot();

        float Pitch { get; }
        float Yaw { get; }
        float Roll { get; }
        float ThrottleInput { get; }
    }
}
