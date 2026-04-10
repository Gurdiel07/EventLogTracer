using System.Text;
using System.Text.RegularExpressions;
using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;

namespace EventLogTracer.Infrastructure.Services;

/// <summary>
/// In-memory search engine supporting text, regex, field:value filters, and
/// boolean operators (AND / OR / NOT) with parenthesis grouping.
///
/// Supported syntax:
///   simple text       → matches Message and Source (case-insensitive contains)
///   "exact phrase"    → quoted exact match in Message and Source
///   /pattern/         → regex match on Message (2-second timeout)
///   source:val        → contains on Source
///   level:Error       → exact EventLevel match
///   eventid:4624      → exact numeric EventId match
///   log:Security      → contains on LogName
///   machine:SERVER    → contains on MachineName
///   AND / OR / NOT    → boolean operators (case-insensitive)
///   adjacent terms    → implicit AND
///   (...)             → grouping; precedence: NOT > AND > OR
/// </summary>
public sealed class SearchEngine : ISearchEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly HashSet<string> ValidFields =
        new(StringComparer.OrdinalIgnoreCase) { "source", "level", "eventid", "log", "machine" };

    // ── Public API ────────────────────────────────────────────────────────────

    public List<EventEntry> Search(List<EventEntry> source, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return source.ToList();

        try
        {
            var node = BuildTree(query);
            return source.Where(e => Evaluate(node, e)).ToList();
        }
        catch
        {
            return new List<EventEntry>();
        }
    }

    public SearchValidationResult Validate(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchValidationResult
            {
                IsValid = false,
                ErrorMessage = "Query cannot be empty."
            };

        try
        {
            var tokens = Tokenize(query);
            var tokenStrings = tokens.Select(t => t.ToString()).ToList();
            new QueryParser(tokens).Parse();
            return new SearchValidationResult { IsValid = true, ParsedTokens = tokenStrings };
        }
        catch (Exception ex)
        {
            return new SearchValidationResult { IsValid = false, ErrorMessage = ex.Message };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SearchNode BuildTree(string query)
    {
        var tokens = Tokenize(query);
        return new QueryParser(tokens).Parse();
    }

    // ── Tokenizer ─────────────────────────────────────────────────────────────

    private enum TokenKind { Term, FieldTerm, Regex, And, Or, Not, OpenParen, CloseParen }

    private sealed class Token
    {
        public TokenKind Kind { get; }
        public string Value { get; }

        public Token(TokenKind kind, string value) { Kind = kind; Value = value; }

        public override string ToString() => $"[{Kind}]{Value}";
    }

    private static List<Token> Tokenize(string query)
    {
        var tokens = new List<Token>();
        int pos = 0;

        while (pos < query.Length)
        {
            if (char.IsWhiteSpace(query[pos])) { pos++; continue; }

            if (query[pos] == '(') { tokens.Add(new Token(TokenKind.OpenParen, "(")); pos++; continue; }
            if (query[pos] == ')') { tokens.Add(new Token(TokenKind.CloseParen, ")")); pos++; continue; }

            // Regex literal: /pattern/
            if (query[pos] == '/')
            {
                pos++; // skip opening /
                var sb = new StringBuilder();
                while (pos < query.Length && query[pos] != '/')
                {
                    if (query[pos] == '\\' && pos + 1 < query.Length && query[pos + 1] == '/')
                    { sb.Append('/'); pos += 2; }
                    else
                    { sb.Append(query[pos++]); }
                }
                if (pos >= query.Length)
                    throw new InvalidOperationException("Unterminated regex literal: missing closing '/'.");
                pos++; // skip closing /
                tokens.Add(new Token(TokenKind.Regex, sb.ToString()));
                continue;
            }

            // Quoted string: "exact phrase"
            if (query[pos] == '"')
            {
                pos++; // skip opening "
                var sb = new StringBuilder();
                while (pos < query.Length && query[pos] != '"')
                {
                    if (query[pos] == '\\' && pos + 1 < query.Length && query[pos + 1] == '"')
                    { sb.Append('"'); pos += 2; }
                    else
                    { sb.Append(query[pos++]); }
                }
                if (pos >= query.Length)
                    throw new InvalidOperationException("Unterminated quoted string: missing closing '\"'.");
                pos++; // skip closing "
                tokens.Add(new Token(TokenKind.Term, sb.ToString()));
                continue;
            }

            // Word: keyword, field:value, or bare term
            {
                var sb = new StringBuilder();
                while (pos < query.Length
                    && !char.IsWhiteSpace(query[pos])
                    && query[pos] != '(' && query[pos] != ')'
                    && query[pos] != '"' && query[pos] != '/')
                {
                    sb.Append(query[pos++]);
                }

                var word = sb.ToString();
                TokenKind kind;

                if (word.Equals("AND", StringComparison.OrdinalIgnoreCase))
                    kind = TokenKind.And;
                else if (word.Equals("OR", StringComparison.OrdinalIgnoreCase))
                    kind = TokenKind.Or;
                else if (word.Equals("NOT", StringComparison.OrdinalIgnoreCase))
                    kind = TokenKind.Not;
                else if (word.Contains(':'))
                    kind = TokenKind.FieldTerm;
                else
                    kind = TokenKind.Term;

                tokens.Add(new Token(kind, word));
            }
        }

        return tokens;
    }

    // ── AST nodes ─────────────────────────────────────────────────────────────

    private abstract class SearchNode { }

    private sealed class TermNode : SearchNode
    {
        public string Value { get; }
        public TermNode(string value) => Value = value;
    }

    private sealed class FieldNode : SearchNode
    {
        public string Field { get; }
        public string Value { get; }
        public FieldNode(string field, string value) { Field = field; Value = value; }
    }

    private sealed class RegexNode : SearchNode
    {
        public string Pattern { get; }
        public RegexNode(string pattern) => Pattern = pattern;
    }

    private sealed class NotNode : SearchNode
    {
        public SearchNode Operand { get; }
        public NotNode(SearchNode operand) => Operand = operand;
    }

    private sealed class BinaryNode : SearchNode
    {
        public bool IsAnd { get; }
        public SearchNode Left { get; }
        public SearchNode Right { get; }
        public BinaryNode(bool isAnd, SearchNode left, SearchNode right)
        { IsAnd = isAnd; Left = left; Right = right; }
    }

    // ── Parser ────────────────────────────────────────────────────────────────
    //
    // Grammar (precedence low→high):
    //   query  = orExpr EOF
    //   orExpr = andExpr (OR andExpr)*
    //   andExpr = notExpr (AND? notExpr)*   ← AND is optional (implicit)
    //   notExpr = NOT notExpr | primary
    //   primary = '(' orExpr ')' | FIELDTERM | REGEX | TERM
    //
    // Precedence: NOT > AND > OR

    private sealed class QueryParser
    {
        private readonly List<Token> _tokens;
        private int _pos;

        public QueryParser(List<Token> tokens) => _tokens = tokens;

        private Token? Current => _pos < _tokens.Count ? _tokens[_pos] : null;

        public SearchNode Parse()
        {
            if (_tokens.Count == 0)
                throw new InvalidOperationException("Query is empty.");

            var node = ParseOrExpr();

            if (Current != null)
                throw new InvalidOperationException(
                    $"Unexpected token '{Current.Value}' — check for unmatched parentheses.");

            return node;
        }

        private SearchNode ParseOrExpr()
        {
            var left = ParseAndExpr();

            while (Current?.Kind == TokenKind.Or)
            {
                _pos++; // consume OR
                var right = ParseAndExpr();
                left = new BinaryNode(false, left, right);
            }

            return left;
        }

        private SearchNode ParseAndExpr()
        {
            var left = ParseNotExpr();

            while (Current != null
                && Current.Kind != TokenKind.Or
                && Current.Kind != TokenKind.CloseParen)
            {
                bool explicitAnd = Current.Kind == TokenKind.And;
                if (explicitAnd) _pos++; // consume explicit AND

                // After consuming AND there must be a right-hand operand
                if (Current == null
                    || Current.Kind == TokenKind.Or
                    || Current.Kind == TokenKind.CloseParen)
                {
                    if (explicitAnd)
                        throw new InvalidOperationException("Expected an expression after 'AND'.");
                    break;
                }

                var right = ParseNotExpr();
                left = new BinaryNode(true, left, right);
            }

            return left;
        }

        private SearchNode ParseNotExpr()
        {
            if (Current?.Kind == TokenKind.Not)
            {
                _pos++; // consume NOT
                if (Current == null)
                    throw new InvalidOperationException("Expected an expression after 'NOT'.");
                return new NotNode(ParseNotExpr()); // right-associative
            }

            return ParsePrimary();
        }

        private SearchNode ParsePrimary()
        {
            if (Current == null)
                throw new InvalidOperationException("Unexpected end of query.");

            switch (Current.Kind)
            {
                case TokenKind.OpenParen:
                {
                    _pos++; // consume (
                    var inner = ParseOrExpr();
                    if (Current?.Kind != TokenKind.CloseParen)
                        throw new InvalidOperationException("Missing closing parenthesis ')'.");
                    _pos++; // consume )
                    return inner;
                }

                case TokenKind.FieldTerm:
                {
                    var token = Current; _pos++;
                    int colonIndex = token.Value.IndexOf(':');
                    string field = token.Value[..colonIndex].ToLowerInvariant();
                    string value = token.Value[(colonIndex + 1)..];

                    if (!ValidFields.Contains(field))
                        throw new InvalidOperationException(
                            $"Unknown field '{field}'. Supported fields: source, level, eventid, log, machine.");

                    if (string.IsNullOrEmpty(value))
                        throw new InvalidOperationException(
                            $"Field '{field}' requires a value after ':'.");

                    if (field == "eventid" && !int.TryParse(value, out _))
                        throw new InvalidOperationException(
                            $"Field 'eventid' requires a numeric value, got '{value}'.");

                    if (field == "level" && !Enum.TryParse<EventLevel>(value, ignoreCase: true, out _))
                        throw new InvalidOperationException(
                            $"Invalid level value '{value}'. Valid values: Verbose, Information, Warning, Error, Critical.");

                    return new FieldNode(field, value);
                }

                case TokenKind.Regex:
                {
                    var token = Current; _pos++;
                    try
                    {
                        // Validate the pattern at parse time; compilation happens again at eval time.
                        _ = new Regex(token.Value, RegexOptions.None, RegexTimeout);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException(
                            $"Invalid regex pattern '{token.Value}': {ex.Message}");
                    }
                    return new RegexNode(token.Value);
                }

                case TokenKind.Term:
                {
                    var token = Current; _pos++;
                    return new TermNode(token.Value);
                }

                default:
                    throw new InvalidOperationException($"Unexpected token '{Current.Value}'.");
            }
        }
    }

    // ── Evaluator ─────────────────────────────────────────────────────────────

    private static bool Evaluate(SearchNode node, EventEntry entry)
    {
        switch (node)
        {
            case TermNode t:
                return ContainsText(entry.Message, t.Value)
                    || ContainsText(entry.Source, t.Value);

            case FieldNode f:
                return EvaluateField(f.Field, f.Value, entry);

            case RegexNode r:
                return EvaluateRegex(r.Pattern, entry.Message);

            case NotNode n:
                return !Evaluate(n.Operand, entry);

            case BinaryNode b:
                return b.IsAnd
                    ? Evaluate(b.Left, entry) && Evaluate(b.Right, entry)
                    : Evaluate(b.Left, entry) || Evaluate(b.Right, entry);

            default:
                return false;
        }
    }

    private static bool EvaluateField(string field, string value, EventEntry entry)
    {
        return field switch
        {
            "source"  => ContainsText(entry.Source, value),
            "level"   => Enum.TryParse<EventLevel>(value, ignoreCase: true, out var lvl)
                         && entry.Level == lvl,
            "eventid" => int.TryParse(value, out var id) && entry.EventId == id,
            "log"     => ContainsText(entry.LogName, value),
            "machine" => ContainsText(entry.MachineName, value),
            _         => false
        };
    }

    private static bool EvaluateRegex(string pattern, string text)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.None, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false; // treat timeout as no-match, never throw
        }
    }

    private static bool ContainsText(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
