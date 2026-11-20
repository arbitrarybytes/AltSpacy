using System.Globalization;

namespace AltSpacy;

/// <summary>Evaluates simple arithmetic typed into the bar: + - * / % ^, parentheses, decimals. "x" also multiplies.</summary>
public static class Calculator
{
    public static double? TryEvaluate(string text)
    {
        var expression = text.Trim().Replace(" ", "").Replace(',', '.').Replace('x', '*').Replace('×', '*').Replace('÷', '/');
        if (expression.StartsWith('=')) expression = expression[1..];
        if (expression.Length < 3 || !expression.Any(char.IsAsciiDigit)) return null;
        if (!expression.Skip(1).Any(c => c is '+' or '-' or '*' or '/' or '%' or '^')) return null;
        if (expression.Any(c => !char.IsAsciiDigit(c) && "+-*/%^().".IndexOf(c) < 0)) return null;

        var parser = new Parser(expression);
        try
        {
            var value = parser.Expression();
            return parser.AtEnd && double.IsFinite(value) ? value : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string Format(double value) =>
        Math.Abs(value) >= 1e15 || (value != 0 && Math.Abs(value) < 1e-6)
            ? value.ToString("G10", CultureInfo.InvariantCulture)
            : Math.Round(value, 10).ToString("#,0.##########", CultureInfo.InvariantCulture);

    /// <summary>Same number without thousands separators, for copying into other apps.</summary>
    public static string Plain(double value) => Math.Round(value, 10).ToString("0.##########", CultureInfo.InvariantCulture);

    private sealed class Parser(string text)
    {
        private int _pos;

        public bool AtEnd => _pos == text.Length;

        public double Expression()
        {
            var value = Term();
            while (!AtEnd && text[_pos] is '+' or '-')
                value = text[_pos++] == '+' ? value + Term() : value - Term();
            return value;
        }

        private double Term()
        {
            var value = Power();
            while (!AtEnd && text[_pos] is '*' or '/' or '%')
            {
                var op = text[_pos++];
                var right = Power();
                value = op switch { '*' => value * right, '/' => value / right, _ => value % right };
            }
            return value;
        }

        private double Power()
        {
            var value = Unary();
            if (!AtEnd && text[_pos] == '^')
            {
                _pos++;
                return Math.Pow(value, Power());
            }
            return value;
        }

        private double Unary()
        {
            if (!AtEnd && text[_pos] is '-' or '+') return text[_pos++] == '-' ? -Unary() : Unary();
            return Primary();
        }

        private double Primary()
        {
            if (!AtEnd && text[_pos] == '(')
            {
                _pos++;
                var value = Expression();
                if (AtEnd || text[_pos++] != ')') throw new FormatException();
                return value;
            }
            var start = _pos;
            while (!AtEnd && (char.IsAsciiDigit(text[_pos]) || text[_pos] == '.')) _pos++;
            return double.Parse(text[start.._pos], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}
