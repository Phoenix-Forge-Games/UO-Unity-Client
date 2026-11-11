using System;
using System.Collections.Generic;
using System.Globalization;

namespace UOGame.Content
{
    /// <summary>
    /// The small formula language our content data uses ({"Expr": "floor(6 * Caster.EvalIntFixed / 50) + 1"}):
    /// numbers, names (with dots: Caster.Magery), + - * / %, comparisons, && || !, c ? a : b, and the functions trunc,
    /// floor, ceil, round, abs, sqrt, min, max, clamp(v, lo, hi), rand(lo, hi) (whole numbers, both ends included) and randf() (0..1).
    /// Formulas are parsed once and cached; names are looked up when evaluated.
    /// </summary>
    public static class Formula
    {
        private static readonly Dictionary<string, Node> Cache = new Dictionary<string, Node>();

        public static double Eval(string text, Func<string, double> lookup, Random random)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            if (!Cache.TryGetValue(text, out Node node))
            {
                var parser = new Parser(text);
                node = parser.Expression();
                Cache[text] = node;
            }

            return node(lookup, random);
        }

        private delegate double Node(Func<string, double> lookup, Random random);

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) => _s = s;

            private void Skip()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                {
                    _i++;
                }
            }

            private bool Eat(string token)
            {
                Skip();
                if (string.CompareOrdinal(_s, _i, token, 0, token.Length) == 0)
                {
                    _i += token.Length;
                    return true;
                }

                return false;
            }

            public Node Expression()
            {
                Node condition = Or();
                if (Eat("?"))
                {
                    Node yes = Expression();
                    if (!Eat(":"))
                    {
                        throw new FormatException($"Expected ':' in {_s}");
                    }

                    Node no = Expression();
                    return (l, r) => condition(l, r) != 0 ? yes(l, r) : no(l, r);
                }

                return condition;
            }

            private Node Or()
            {
                Node left = And();
                while (Eat("||"))
                {
                    Node a = left, b = And();
                    left = (l, r) => a(l, r) != 0 || b(l, r) != 0 ? 1 : 0;
                }

                return left;
            }

            private Node And()
            {
                Node left = Compare();
                while (Eat("&&"))
                {
                    Node a = left, b = Compare();
                    left = (l, r) => a(l, r) != 0 && b(l, r) != 0 ? 1 : 0;
                }

                return left;
            }

            private Node Compare()
            {
                Node left = Sum();
                while (true)
                {
                    Node a = left;
                    if (Eat("==")) { Node b = Sum(); left = (l, r) => a(l, r) == b(l, r) ? 1 : 0; }
                    else if (Eat("!=")) { Node b = Sum(); left = (l, r) => a(l, r) != b(l, r) ? 1 : 0; }
                    else if (Eat("<=")) { Node b = Sum(); left = (l, r) => a(l, r) <= b(l, r) ? 1 : 0; }
                    else if (Eat(">=")) { Node b = Sum(); left = (l, r) => a(l, r) >= b(l, r) ? 1 : 0; }
                    else if (Eat("<")) { Node b = Sum(); left = (l, r) => a(l, r) < b(l, r) ? 1 : 0; }
                    else if (Eat(">")) { Node b = Sum(); left = (l, r) => a(l, r) > b(l, r) ? 1 : 0; }
                    else return left;
                }
            }

            private Node Sum()
            {
                Node left = Product();
                while (true)
                {
                    Node a = left;
                    if (Eat("+")) { Node b = Product(); left = (l, r) => a(l, r) + b(l, r); }
                    else if (Eat("-")) { Node b = Product(); left = (l, r) => a(l, r) - b(l, r); }
                    else return left;
                }
            }

            private Node Product()
            {
                Node left = Unary();
                while (true)
                {
                    Node a = left;
                    if (Eat("*")) { Node b = Unary(); left = (l, r) => a(l, r) * b(l, r); }
                    else if (Eat("/")) { Node b = Unary(); left = (l, r) => { double d = b(l, r); return d == 0 ? 0 : a(l, r) / d; }; }
                    else if (Eat("%")) { Node b = Unary(); left = (l, r) => { double d = b(l, r); return d == 0 ? 0 : a(l, r) % d; }; }
                    else return left;
                }
            }

            private Node Unary()
            {
                if (Eat("-")) { Node a = Unary(); return (l, r) => -a(l, r); }
                if (Eat("!")) { Node a = Unary(); return (l, r) => a(l, r) == 0 ? 1 : 0; }
                return Atom();
            }

            private Node Atom()
            {
                Skip();
                if (Eat("("))
                {
                    Node inner = Expression();
                    Eat(")");
                    return inner;
                }

                int start = _i;
                if (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
                {
                    while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
                    {
                        _i++;
                    }

                    double value = double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture);
                    return (l, r) => value;
                }

                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '.'))
                {
                    _i++;
                }

                string name = _s.Substring(start, _i - start);
                if (name.Length == 0)
                {
                    throw new FormatException($"Unexpected '{(_i < _s.Length ? _s[_i] : ' ')}' in {_s}");
                }

                if (Eat("("))
                {
                    var args = new List<Node>();
                    if (!Eat(")"))
                    {
                        do
                        {
                            args.Add(Expression());
                        }
                        while (Eat(","));
                        Eat(")");
                    }

                    return Call(name, args);
                }

                if (name == "true") return (l, r) => 1;
                if (name == "false") return (l, r) => 0;
                return (l, r) => l(name);
            }

            private Node Call(string name, List<Node> a)
            {
                Node Arg(int i) => i < a.Count ? a[i] : (l, r) => 0;
                switch (name)
                {
                    case "trunc": return (l, r) => Math.Truncate(Arg(0)(l, r));
                    case "floor": return (l, r) => Math.Floor(Arg(0)(l, r));
                    case "ceil": return (l, r) => Math.Ceiling(Arg(0)(l, r));
                    case "round": return (l, r) => Math.Round(Arg(0)(l, r));
                    case "abs": return (l, r) => Math.Abs(Arg(0)(l, r));
                    case "sqrt": return (l, r) => Math.Sqrt(Math.Max(0, Arg(0)(l, r)));
                    case "min": return (l, r) => { double m = Arg(0)(l, r); for (int i = 1; i < a.Count; i++) m = Math.Min(m, a[i](l, r)); return m; };
                    case "max": return (l, r) => { double m = Arg(0)(l, r); for (int i = 1; i < a.Count; i++) m = Math.Max(m, a[i](l, r)); return m; };
                    case "rand": return (l, r) => { int lo = (int)Arg(0)(l, r), hi = (int)Arg(1)(l, r); return hi < lo ? lo : r.Next(lo, hi + 1); };
                    case "randf": return (l, r) => r.NextDouble();
                    case "clamp": return (l, r) => Math.Max(Arg(1)(l, r), Math.Min(Arg(2)(l, r), Arg(0)(l, r)));
                    default: throw new FormatException($"Unknown function {name} in {_s}");
                }
            }
        }
    }
}
