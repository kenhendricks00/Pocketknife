using System.Globalization;

namespace Pocketknife.Core.Launcher;

/// <param name="Display">The result as shown, with the culture's digit grouping.</param>
/// <param name="CopyText">The result as copied: no grouping, so it pastes cleanly into other apps.</param>
public sealed record Calculation(double Value, string Display, string CopyText);

/// <summary>
/// Evaluates launcher text as arithmetic with a small tokenizer and recursive-descent parser (no eval).
/// Supports + - * / ^, parentheses, unary minus, %, "x% of y", sqrt abs round floor ceil min max sin cos tan
/// log ln, pi and e, hex/binary/octal literals and "to hex/bin/oct/dec". Text that isn't clearly a
/// calculation (plain words, a lone number, a bang) gives null, as do errors such as dividing by zero.
/// </summary>
public static class Calculator
{
    private const int MaxLength = 256;
    private const int MaxDepth = 64;

    public static Calculation? TryEvaluate(string text, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return null;
        }

        try
        {
            var parser = new Parser(Tokenize(text));
            var (value, numberBase) = parser.ParseAll();
            if (!parser.IsCalculation || double.IsNaN(value) || double.IsInfinity(value))
            {
                return null;
            }

            if (numberBase is { } b and not 10)
            {
                var based = FormatBase(value, b);
                return based is null ? null : new Calculation(value, based, based);
            }

            return new Calculation(value, Format(value, culture, grouping: true), Format(value, culture, grouping: false));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Rounds to 15 significant digits so 0.1 + 0.2 shows as 0.3, using the culture's separators.</summary>
    internal static string Format(double value, CultureInfo culture, bool grouping)
    {
        var rounded = double.Parse(value.ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (rounded == 0)
        {
            return 0.ToString(culture);
        }

        var size = Math.Abs(rounded);
        if (size >= 1e15 || size < 1e-9)
        {
            return rounded.ToString("G15", culture);
        }

        return rounded.ToString(grouping ? "#,##0.##############" : "0.##############", culture);
    }

    private static string? FormatBase(double value, int numberBase)
    {
        if (value != Math.Floor(value) || Math.Abs(value) > 9007199254740992)
        {
            return null;
        }

        var whole = (long)value;
        var sign = whole < 0 ? "-" : "";
        var magnitude = Math.Abs(whole);
        return numberBase switch
        {
            16 => $"{sign}0x{magnitude:X}",
            2 => $"{sign}0b{Convert.ToString(magnitude, 2)}",
            _ => $"{sign}0o{Convert.ToString(magnitude, 8)}",
        };
    }

    private enum Kind
    {
        Number,
        Word,
        Symbol,
        End,
    }

    private readonly record struct Token(Kind Kind, double Number = 0, string Text = "", bool BaseLiteral = false);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                tokens.Add(ReadNumber(text, ref i));
            }
            else if (char.IsAsciiLetter(c) || c == 'π')
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiLetter(text[i]) || text[i] == 'π'))
                {
                    i++;
                }

                tokens.Add(new Token(Kind.Word, Text: text[start..i].ToLowerInvariant()));
            }
            else
            {
                var symbol = c switch
                {
                    '+' or '-' or '*' or '/' or '^' or '(' or ')' or '%' or ',' => c,
                    '×' => '*',
                    '÷' => '/',
                    '−' => '-',
                    ';' => ',',
                    _ => throw new FormatException($"Unexpected '{c}'."),
                };
                tokens.Add(new Token(Kind.Symbol, Text: symbol.ToString()));
                i++;
            }
        }

        tokens.Add(new Token(Kind.End));
        return tokens;
    }

    private static Token ReadNumber(string text, ref int i)
    {
        if (text[i] == '0' && i + 2 < text.Length && text[i + 1] is 'x' or 'X' or 'b' or 'B' or 'o' or 'O')
        {
            var numberBase = char.ToLowerInvariant(text[i + 1]) switch { 'x' => 16, 'b' => 2, _ => 8 };
            var start = i += 2;
            while (i < text.Length && Uri.IsHexDigit(text[i]))
            {
                i++;
            }

            if (i == start || i - start > (numberBase == 2 ? 62 : numberBase == 8 ? 20 : 15))
            {
                throw new FormatException("Bad literal.");
            }

            return new Token(Kind.Number, Convert.ToInt64(text[start..i], numberBase), BaseLiteral: true);
        }

        var begin = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
        }

        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }
        }

        if (i + 1 < text.Length && text[i] is 'e' or 'E'
            && (char.IsAsciiDigit(text[i + 1]) || (text[i + 1] is '+' or '-' && i + 2 < text.Length && char.IsAsciiDigit(text[i + 2]))))
        {
            i += 2;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }
        }

        if (i < text.Length && text[i] == '.')
        {
            throw new FormatException("Two decimal points.");
        }

        return new Token(Kind.Number, double.Parse(text[begin..i], NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    private sealed class Parser(List<Token> tokens)
    {
        private static readonly Dictionary<string, double> s_constants = new() { ["pi"] = Math.PI, ["π"] = Math.PI, ["e"] = Math.E };
        private static readonly Dictionary<string, int> s_bases = new()
        {
            ["hex"] = 16,
            ["hexadecimal"] = 16,
            ["bin"] = 2,
            ["binary"] = 2,
            ["oct"] = 8,
            ["octal"] = 8,
            ["dec"] = 10,
            ["decimal"] = 10,
        };

        private int _position;
        private int _depth;

        /// <summary>True once something beyond a bare number or constant was evaluated.</summary>
        public bool IsCalculation { get; private set; }

        private Token Current => tokens[_position];

        public (double Value, int? Base) ParseAll()
        {
            var value = Expression().Value;
            int? numberBase = null;
            if (Current is { Kind: Kind.Word, Text: "to" or "in" or "as" })
            {
                _position++;
                if (Current.Kind != Kind.Word || !s_bases.TryGetValue(Current.Text, out var b))
                {
                    throw new FormatException("Unknown base.");
                }

                numberBase = b;
                IsCalculation = true;
                _position++;
            }

            if (Current.Kind != Kind.End)
            {
                throw new FormatException("Unexpected text.");
            }

            return (value, numberBase);
        }

        // A value flagged as a percentage ("10%") makes "200 + 10%" mean 200 plus 10 percent of 200.
        private (double Value, bool Percent) Expression()
        {
            Enter();
            var left = Term();
            while (IsSymbol("+") || IsSymbol("-"))
            {
                var plus = Current.Text == "+";
                _position++;
                var right = Term();
                var change = right.Percent ? left.Value * right.Value : right.Value;
                left = (plus ? left.Value + change : left.Value - change, false);
                IsCalculation = true;
            }

            _depth--;
            return left;
        }

        private (double Value, bool Percent) Term()
        {
            var left = Unary();
            while (true)
            {
                if (IsSymbol("*") || IsSymbol("/"))
                {
                    var multiply = Current.Text == "*";
                    _position++;
                    var right = Unary().Value;
                    left = (multiply ? left.Value * right : Divide(left.Value, right), false);
                }
                else if (IsSymbol("(") || (Current.Kind == Kind.Word && IsValueWord(Current.Text)))
                {
                    left = (left.Value * Unary().Value, false);
                }
                else
                {
                    return left;
                }

                IsCalculation = true;
            }
        }

        private (double Value, bool Percent) Unary()
        {
            Enter();
            (double, bool) result;
            if (IsSymbol("-") || IsSymbol("+"))
            {
                var negate = Current.Text == "-";
                _position++;
                var (value, percent) = Unary();
                result = (negate ? -value : value, percent);
            }
            else
            {
                result = Power();
            }

            _depth--;
            return result;
        }

        private (double Value, bool Percent) Power()
        {
            var (value, percent) = Postfix();
            if (IsSymbol("^"))
            {
                _position++;
                IsCalculation = true;
                return (Math.Pow(value, Unary().Value), false);
            }

            return (value, percent);
        }

        private (double Value, bool Percent) Postfix()
        {
            var value = Primary();
            if (!IsSymbol("%"))
            {
                return (value, false);
            }

            _position++;
            IsCalculation = true;
            if (Current is { Kind: Kind.Word, Text: "of" })
            {
                _position++;
                return (value / 100 * Unary().Value, false);
            }

            return (value / 100, true);
        }

        private double Primary()
        {
            var token = Current;
            switch (token.Kind)
            {
                case Kind.Number:
                    _position++;
                    IsCalculation |= token.BaseLiteral;
                    return token.Number;
                case Kind.Symbol when token.Text == "(":
                    _position++;
                    var inner = Expression().Value;
                    Expect(")");
                    return inner;
                case Kind.Word when s_constants.TryGetValue(token.Text, out var constant):
                    _position++;
                    return constant;
                case Kind.Word when IsFunction(token.Text):
                    _position++;
                    return Call(token.Text);
                default:
                    throw new FormatException("Expected a number.");
            }
        }

        private double Call(string name)
        {
            Expect("(");
            var args = new List<double> { Expression().Value };
            while (IsSymbol(","))
            {
                _position++;
                args.Add(Expression().Value);
            }

            Expect(")");
            IsCalculation = true;
            return (name, args.Count) switch
            {
                ("min", _) => args.Min(),
                ("max", _) => args.Max(),
                ("round", 2) when args[1] == Math.Floor(args[1]) && args[1] is >= 0 and <= 15 =>
                    Math.Round(args[0], (int)args[1], MidpointRounding.AwayFromZero),
                ("round", 1) => Math.Round(args[0], MidpointRounding.AwayFromZero),
                ("sqrt", 1) => Math.Sqrt(args[0]),
                ("abs", 1) => Math.Abs(args[0]),
                ("floor", 1) => Math.Floor(args[0]),
                ("ceil", 1) => Math.Ceiling(args[0]),
                ("sin", 1) => Clean(Math.Sin(args[0])),
                ("cos", 1) => Clean(Math.Cos(args[0])),
                ("tan", 1) => Clean(Math.Tan(args[0])),
                ("log", 1) => Math.Log10(args[0]),
                ("ln", 1) => Math.Log(args[0]),
                _ => throw new FormatException($"Wrong arguments for {name}."),
            };
        }

        // sin(pi) is 1.2e-16 in floating point; anything that small next to the inputs used here is zero.
        private static double Clean(double value) => Math.Abs(value) < 1e-12 ? 0 : value;

        private static double Divide(double left, double right) =>
            right == 0 ? throw new FormatException("Division by zero.") : left / right;

        private static bool IsFunction(string word) =>
            word is "sqrt" or "abs" or "round" or "floor" or "ceil" or "min" or "max" or "sin" or "cos" or "tan" or "log" or "ln";

        private static bool IsValueWord(string word) => s_constants.ContainsKey(word) || IsFunction(word);

        private bool IsSymbol(string symbol) => Current.Kind == Kind.Symbol && Current.Text == symbol;

        private void Expect(string symbol)
        {
            if (!IsSymbol(symbol))
            {
                throw new FormatException($"Expected '{symbol}'.");
            }

            _position++;
        }

        private void Enter()
        {
            if (++_depth > MaxDepth)
            {
                throw new FormatException("Too deeply nested.");
            }
        }
    }
}
