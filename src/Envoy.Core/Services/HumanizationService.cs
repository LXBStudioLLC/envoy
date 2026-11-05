namespace Envoy.Core.Services;

public record Point(double X, double Y);

public class HumanizationService
{
    private readonly int _typingVariance;
    private readonly int _mousePathBaseSteps;

    /// <summary>
    /// APP-16: the settings knobs finally wired. Defaults match the previous
    /// hard-coded values (variance 35, path steps 15–20); a settings value
    /// shifts the base path-step count and the typing variance.
    /// </summary>
    public HumanizationService(int typingVariance = 35, int mousePathBaseSteps = 15)
    {
        _typingVariance = Math.Clamp(typingVariance, 0, 150);
        _mousePathBaseSteps = Math.Clamp(mousePathBaseSteps, 5, 120);
    }

    public int GetTypingDelay()
    {
        return Math.Max(30, 85 + (int)(_typingVariance * NormalRandom()));
    }

    public int GetClickDelay()
    {
        return 150 + Random.Shared.Next(0, 250);
    }

    public int GetMicroDelay()
    {
        return 5 + Random.Shared.Next(0, 15);
    }

    public (double X, double Y) GetClickTarget(double elementX, double elementY, double width, double height)
    {
        var marginX = width * 0.2;
        var marginY = height * 0.2;
        var x = elementX + marginX + Random.Shared.NextDouble() * (width - marginX * 2);
        var y = elementY + marginY + Random.Shared.NextDouble() * (height - marginY * 2);
        return (x, y);
    }

    public List<Point> GenerateMousePath(double fromX, double fromY, double toX, double toY)
    {
        var points = new List<Point>();
        var steps = _mousePathBaseSteps + Random.Shared.Next(0, Math.Max(1, _mousePathBaseSteps / 2));

        var midX = (fromX + toX) / 2;
        var midY = (fromY + toY) / 2;
        var offsetX = (Random.Shared.NextDouble() - 0.5) * Math.Abs(toX - fromX) * 0.3;
        var offsetY = (Random.Shared.NextDouble() - 0.5) * Math.Abs(toY - fromY) * 0.3;
        var cpX = midX + offsetX;
        var cpY = midY + offsetY;

        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            var x = Math.Pow(1 - t, 2) * fromX + 2 * (1 - t) * t * cpX + Math.Pow(t, 2) * toX;
            var y = Math.Pow(1 - t, 2) * fromY + 2 * (1 - t) * t * cpY + Math.Pow(t, 2) * toY;
            points.Add(new Point(x, y));
        }

        return points;
    }

    private static double NormalRandom()
    {
        // Box-Muller transform; Random.Shared is thread-safe so no lock needed.
        var u1 = 1.0 - Random.Shared.NextDouble();
        var u2 = 1.0 - Random.Shared.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
