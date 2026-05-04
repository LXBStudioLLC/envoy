namespace Envoy.Core.Services;

public record Point(double X, double Y);

public class HumanizationService
{
    private static readonly Random _sharedRandom = new();

    private int NextRandom(int min, int max)
    {
        lock (_sharedRandom)
        {
            return _sharedRandom.Next(min, max);
        }
    }

    private double NextDouble()
    {
        lock (_sharedRandom)
        {
            return _sharedRandom.NextDouble();
        }
    }

    public int GetTypingDelay()
    {
        var baseDelay = 85;
        var variance = 35;
        return Math.Max(30, baseDelay + (int)(variance * NormalRandom()));
    }

    public int GetClickDelay()
    {
        return 150 + NextRandom(0, 250);
    }

    public int GetMicroDelay()
    {
        return 5 + NextRandom(0, 15);
    }

    public int GetScrollDelay()
    {
        return 200 + NextRandom(0, 800);
    }

    public (double X, double Y) GetClickTarget(double elementX, double elementY, double width, double height)
    {
        var marginX = width * 0.2;
        var marginY = height * 0.2;
        var x = elementX + marginX + NextDouble() * (width - marginX * 2);
        var y = elementY + marginY + NextDouble() * (height - marginY * 2);
        return (x, y);
    }

    public List<Point> GenerateMousePath(double fromX, double fromY, double toX, double toY)
    {
        var points = new List<Point>();
        var steps = 15 + NextRandom(0, 20);

        var midX = (fromX + toX) / 2;
        var midY = (fromY + toY) / 2;
        var offsetX = (NextDouble() - 0.5) * Math.Abs(toX - fromX) * 0.3;
        var offsetY = (NextDouble() - 0.5) * Math.Abs(toY - fromY) * 0.3;
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

    private double NormalRandom()
    {
        double u1, u2;
        lock (_sharedRandom)
        {
            u1 = 1.0 - _sharedRandom.NextDouble();
            u2 = 1.0 - _sharedRandom.NextDouble();
        }
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
