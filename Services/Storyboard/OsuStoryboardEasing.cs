using System;

namespace BenyComes.Services.Storyboard;

/// <summary>
/// Implementa las 35 curvas de easing oficiales de osu! (0..34) para comandos de Storyboard.
/// </summary>
public static class OsuStoryboardEasing
{
    private const double ElasticConst = (2.0 * Math.PI) / 3.0;
    private const double ElasticConst2 = (2.0 * Math.PI) / 4.5;
    private const double BackConst1 = 1.70158;
    private const double BackConst2 = BackConst1 * 1.525;

    public static float Apply(int easing, double t)
    {
        if (t <= 0.0) return 0f;
        if (t >= 1.0) return 1f;

        double res = easing switch
        {
            0 => t, // Linear
            1 or 4 => EaseOutQuad(t),
            2 or 3 => EaseInQuad(t),
            5 => EaseInOutQuad(t),
            6 => EaseInCubic(t),
            7 => EaseOutCubic(t),
            8 => EaseInOutCubic(t),
            9 => EaseInQuart(t),
            10 => EaseOutQuart(t),
            11 => EaseInOutQuart(t),
            12 => EaseInQuint(t),
            13 => EaseOutQuint(t),
            14 => EaseInOutQuint(t),
            15 => EaseInSine(t),
            16 => EaseOutSine(t),
            17 => EaseInOutSine(t),
            18 => EaseInExpo(t),
            19 => EaseOutExpo(t),
            20 => EaseInOutExpo(t),
            21 => EaseInCirc(t),
            22 => EaseOutCirc(t),
            23 => EaseInOutCirc(t),
            24 => EaseInElastic(t),
            25 => EaseOutElastic(t),
            26 => EaseOutElasticHalf(t),
            27 => EaseOutElasticQuarter(t),
            28 => EaseInOutElastic(t),
            29 => EaseInBack(t),
            30 => EaseOutBack(t),
            31 => EaseInOutBack(t),
            32 => EaseInBounce(t),
            33 => EaseOutBounce(t),
            34 => EaseInOutBounce(t),
            _ => t
        };

        return (float)res;
    }

    private static double EaseInQuad(double t) => t * t;
    private static double EaseOutQuad(double t) => t * (2.0 - t);
    private static double EaseInOutQuad(double t) => t < 0.5 ? 2.0 * t * t : -1.0 + (4.0 - 2.0 * t) * t;

    private static double EaseInCubic(double t) => t * t * t;
    private static double EaseOutCubic(double t)
    {
        double u = t - 1.0;
        return u * u * u + 1.0;
    }
    private static double EaseInOutCubic(double t) =>
        t < 0.5 ? 4.0 * t * t * t : (t - 1.0) * (2.0 * t - 2.0) * (2.0 * t - 2.0) + 1.0;

    private static double EaseInQuart(double t) => t * t * t * t;
    private static double EaseOutQuart(double t)
    {
        double u = t - 1.0;
        return 1.0 - u * u * u * u;
    }
    private static double EaseInOutQuart(double t)
    {
        if (t < 0.5) return 8.0 * t * t * t * t;
        double u = t - 1.0;
        return 1.0 - 8.0 * u * u * u * u;
    }

    private static double EaseInQuint(double t) => t * t * t * t * t;
    private static double EaseOutQuint(double t)
    {
        double u = t - 1.0;
        return 1.0 + u * u * u * u * u;
    }
    private static double EaseInOutQuint(double t)
    {
        if (t < 0.5) return 16.0 * t * t * t * t * t;
        double u = t - 1.0;
        return 1.0 + 16.0 * u * u * u * u * u;
    }

    private static double EaseInSine(double t) => 1.0 - Math.Cos((t * Math.PI) * 0.5);
    private static double EaseOutSine(double t) => Math.Sin((t * Math.PI) * 0.5);
    private static double EaseInOutSine(double t) => -0.5 * (Math.Cos(Math.PI * t) - 1.0);

    private static double EaseInExpo(double t) => Math.Pow(2.0, 10.0 * (t - 1.0));
    private static double EaseOutExpo(double t) => 1.0 - Math.Pow(2.0, -10.0 * t);
    private static double EaseInOutExpo(double t) =>
        t < 0.5 ? 0.5 * Math.Pow(2.0, 20.0 * t - 10.0) : 1.0 - 0.5 * Math.Pow(2.0, -20.0 * t + 10.0);

    private static double EaseInCirc(double t) => 1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - t * t));
    private static double EaseOutCirc(double t)
    {
        double u = t - 1.0;
        return Math.Sqrt(Math.Max(0.0, 1.0 - u * u));
    }
    private static double EaseInOutCirc(double t) =>
        t < 0.5
            ? 0.5 * (1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - 4.0 * t * t)))
            : 0.5 * (Math.Sqrt(Math.Max(0.0, 1.0 - (2.0 * t - 2.0) * (2.0 * t - 2.0))) + 1.0);

    private static double EaseInElastic(double t) =>
        -Math.Pow(2.0, 10.0 * t - 10.0) * Math.Sin((t * 10.0 - 10.75) * ElasticConst);

    private static double EaseOutElastic(double t) =>
        Math.Pow(2.0, -10.0 * t) * Math.Sin((t * 10.0 - 0.75) * ElasticConst) + 1.0;

    private static double EaseOutElasticHalf(double t) =>
        Math.Pow(2.0, -10.0 * t) * Math.Sin((0.5 * t * 10.0 - 0.75) * ElasticConst) + 1.0;

    private static double EaseOutElasticQuarter(double t) =>
        Math.Pow(2.0, -10.0 * t) * Math.Sin((0.25 * t * 10.0 - 0.75) * ElasticConst) + 1.0;

    private static double EaseInOutElastic(double t) =>
        t < 0.5
            ? -0.5 * (Math.Pow(2.0, 20.0 * t - 10.0) * Math.Sin((20.0 * t - 11.125) * ElasticConst2))
            : 0.5 * (Math.Pow(2.0, -20.0 * t + 10.0) * Math.Sin((20.0 * t - 11.125) * ElasticConst2)) + 1.0;

    private static double EaseInBack(double t) =>
        (BackConst1 + 1.0) * t * t * t - BackConst1 * t * t;

    private static double EaseOutBack(double t)
    {
        double u = t - 1.0;
        return 1.0 + (BackConst1 + 1.0) * u * u * u + BackConst1 * u * u;
    }

    private static double EaseInOutBack(double t) =>
        t < 0.5
            ? 0.5 * ((2.0 * t) * (2.0 * t) * ((BackConst2 + 1.0) * 2.0 * t - BackConst2))
            : 0.5 * ((2.0 * t - 2.0) * (2.0 * t - 2.0) * ((BackConst2 + 1.0) * (t * 2.0 - 2.0) + BackConst2) + 2.0);

    private static double EaseInBounce(double t) => 1.0 - EaseOutBounce(1.0 - t);

    private static double EaseOutBounce(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;

        if (t < 1.0 / d1)
            return n1 * t * t;
        if (t < 2.0 / d1)
        {
            t -= 1.5 / d1;
            return n1 * t * t + 0.75;
        }
        if (t < 2.5 / d1)
        {
            t -= 2.25 / d1;
            return n1 * t * t + 0.9375;
        }
        t -= 2.625 / d1;
        return n1 * t * t + 0.984375;
    }

    private static double EaseInOutBounce(double t) =>
        t < 0.5
            ? 0.5 * (1.0 - EaseOutBounce(1.0 - 2.0 * t))
            : 0.5 * (1.0 + EaseOutBounce(2.0 * t - 1.0));
}
