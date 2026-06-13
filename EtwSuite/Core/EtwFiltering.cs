using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EtwSuite.Core;

public enum EtwFilterMode
{
    Basic,
    SQL
}

public sealed class EtwCompiledFilter<T>
{
    private EtwCompiledFilter(Func<T, bool> matches, string? errorMessage)
    {
        Matches = matches;
        ErrorMessage = errorMessage;
    }

    public Func<T, bool> Matches { get; }

    public string? ErrorMessage { get; }

    public bool IsValid => ErrorMessage is null;

    public static EtwCompiledFilter<T> Valid(Func<T, bool> matches)
    {
        return new EtwCompiledFilter<T>(matches, null);
    }

    public static EtwCompiledFilter<T> Invalid(string errorMessage)
    {
        return new EtwCompiledFilter<T>(_ => false, errorMessage);
    }
}

public static class EtwFilterCompiler
{
    public static EtwCompiledFilter<EtwProviderInfo> CompileProviderFilter(EtwFilterMode mode, string filterText)
    {
        return mode == EtwFilterMode.SQL
            ? SqlFilterParser<EtwProviderInfo>.Compile(filterText, ProviderFieldResolver.Resolve, ProviderFieldResolver.IsKnownField)
            : CompileBasicProviderFilter(filterText);
    }

    public static EtwCompiledFilter<EtwLiveEventRecord> CompileEventFilter(EtwFilterMode mode, string filterText)
    {
        return mode == EtwFilterMode.SQL
            ? SqlFilterParser<EtwLiveEventRecord>.Compile(filterText, EventFieldResolver.Resolve, EventFieldResolver.IsKnownField)
            : CompileBasicEventFilter(filterText);
    }

    private static EtwCompiledFilter<EtwProviderInfo> CompileBasicProviderFilter(string filterText)
    {
        string pattern = filterText.Trim();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return EtwCompiledFilter<EtwProviderInfo>.Valid(_ => true);
        }

        bool hasWildcard = HasAppWildcard(pattern);
        return EtwCompiledFilter<EtwProviderInfo>.Valid(provider =>
            MatchesBasic(provider.Name, pattern, hasWildcard) ||
            MatchesBasic(provider.Id.ToString("D"), pattern, hasWildcard));
    }

    private static EtwCompiledFilter<EtwLiveEventRecord> CompileBasicEventFilter(string filterText)
    {
        string pattern = filterText.Trim();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return EtwCompiledFilter<EtwLiveEventRecord>.Valid(_ => true);
        }

        bool hasWildcard = HasAppWildcard(pattern);
        return EtwCompiledFilter<EtwLiveEventRecord>.Valid(record =>
            MatchesBasic(record.ProviderName, pattern, hasWildcard) ||
            MatchesBasic(record.ProviderId.ToString("D"), pattern, hasWildcard) ||
            MatchesBasic(record.EventName, pattern, hasWildcard) ||
            MatchesBasic(record.EventId.ToString(CultureInfo.InvariantCulture), pattern, hasWildcard) ||
            MatchesBasic(record.Version.ToString(CultureInfo.InvariantCulture), pattern, hasWildcard) ||
            MatchesBasic(record.Opcode.ToString(CultureInfo.InvariantCulture), pattern, hasWildcard) ||
            MatchesBasic(record.Level.ToString(CultureInfo.InvariantCulture), pattern, hasWildcard) ||
            MatchesBasic(record.ProcessId.ToString(CultureInfo.InvariantCulture), pattern, hasWildcard) ||
            MatchesBasic(record.ProcessName, pattern, hasWildcard) ||
            MatchesBasic(record.ThreadId.ToString(CultureInfo.InvariantCulture), pattern, hasWildcard) ||
            record.Payload.Any(payload =>
                MatchesBasic(payload.Name, pattern, hasWildcard) ||
                MatchesBasic(payload.Type, pattern, hasWildcard) ||
                MatchesBasic(payload.Value, pattern, hasWildcard)));
    }

    private static bool MatchesBasic(string value, string pattern, bool hasWildcard)
    {
        return hasWildcard
            ? WildcardMatcher.IsMatch(value, pattern, appWildcardsOnly: true)
            : value.Contains(pattern, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool HasAppWildcard(string value)
    {
        return value.Contains('*', StringComparison.Ordinal) || value.Contains('?', StringComparison.Ordinal);
    }

    private static class ProviderFieldResolver
    {
        public static bool Resolve(EtwProviderInfo provider, string fieldName, out FilterValue value)
        {
            switch (NormalizeFieldName(fieldName))
            {
                case "name":
                    value = FilterValue.String(provider.Name);
                    return true;
                case "id":
                case "guid":
                    value = FilterValue.String(provider.Id.ToString("D"));
                    return true;
                case "schema_source":
                    value = FilterValue.String(provider.SchemaSource.ToString());
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        public static bool IsKnownField(string fieldName)
        {
            return NormalizeFieldName(fieldName) is "name" or "id" or "guid" or "schema_source";
        }
    }

    private static class EventFieldResolver
    {
        public static bool Resolve(EtwLiveEventRecord record, string fieldName, out FilterValue value)
        {
            string normalized = NormalizeFieldName(fieldName);
            if (normalized.StartsWith("payload.", StringComparison.Ordinal))
            {
                string payloadName = normalized["payload.".Length..];
                EtwPayloadValue? payload = record.Payload.FirstOrDefault(candidate =>
                    string.Equals(NormalizeFieldName(candidate.Name), payloadName, StringComparison.Ordinal));
                if (payload is null)
                {
                    value = default;
                    return false;
                }

                value = FilterValue.String(payload.Value);
                return true;
            }

            switch (normalized)
            {
                case "provider":
                    value = FilterValue.String(record.ProviderName);
                    return true;
                case "provider_id":
                    value = FilterValue.String(record.ProviderId.ToString("D"));
                    return true;
                case "event":
                case "event_name":
                    value = FilterValue.String(record.EventName);
                    return true;
                case "id":
                case "event_id":
                    value = FilterValue.Numeric(record.EventId);
                    return true;
                case "version":
                    value = FilterValue.Numeric(record.Version);
                    return true;
                case "opcode":
                    value = FilterValue.Numeric(record.Opcode);
                    return true;
                case "level":
                    value = FilterValue.Numeric(record.Level);
                    return true;
                case "pid":
                case "process_id":
                    value = FilterValue.Numeric(record.ProcessId);
                    return true;
                case "process":
                case "process_name":
                    value = FilterValue.String(record.ProcessName);
                    return true;
                case "tid":
                case "thread_id":
                    value = FilterValue.Numeric(record.ThreadId);
                    return true;
                case "payload":
                    value = FilterValue.String(string.Join(
                        " ",
                        record.Payload.Select(payload => string.Create(
                            CultureInfo.InvariantCulture,
                            $"{payload.Name} {payload.Type} {payload.Value}"))));
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        public static bool IsKnownField(string fieldName)
        {
            string normalized = NormalizeFieldName(fieldName);
            return normalized.StartsWith("payload.", StringComparison.Ordinal) ||
                normalized is "provider" or "provider_id" or "event" or "event_name" or
                "id" or "event_id" or "version" or "opcode" or "level" or "pid" or
                "process_id" or "process" or "process_name" or "tid" or "thread_id" or "payload";
        }
    }

    private static string NormalizeFieldName(string fieldName)
    {
        return fieldName.Trim().Replace('-', '_').ToLowerInvariant();
    }

    private readonly record struct FilterValue(string Text, decimal? Number)
    {
        public static FilterValue String(string value)
        {
            return new FilterValue(value, null);
        }

        public static FilterValue Numeric(decimal value)
        {
            return new FilterValue(value.ToString(CultureInfo.InvariantCulture), value);
        }
    }

    private enum SqlTokenKind
    {
        Identifier,
        String,
        Number,
        And,
        Or,
        Not,
        Like,
        Where,
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
        OpenParen,
        CloseParen,
        End
    }

    private readonly record struct SqlToken(SqlTokenKind Kind, string Text, int Position);

    private delegate bool FieldResolver<T>(T item, string fieldName, out FilterValue value);

    private sealed class SqlFilterParser<T>
    {
        private readonly IReadOnlyList<SqlToken> _tokens;
        private readonly Func<T, string, (bool Found, FilterValue Value)> _resolveField;
        private readonly Predicate<string> _isKnownField;
        private int _position;

        private SqlFilterParser(
            IReadOnlyList<SqlToken> tokens,
            Func<T, string, (bool Found, FilterValue Value)> resolveField,
            Predicate<string> isKnownField)
        {
            _tokens = tokens;
            _resolveField = resolveField;
            _isKnownField = isKnownField;
        }

        public static EtwCompiledFilter<T> Compile(
            string filterText,
            FieldResolver<T> resolveField,
            Predicate<string> isKnownField)
        {
            string text = filterText.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return EtwCompiledFilter<T>.Valid(_ => true);
            }

            if (!TryTokenize(text, out IReadOnlyList<SqlToken> tokens, out string? tokenizeError))
            {
                return EtwCompiledFilter<T>.Invalid(tokenizeError);
            }

            var parser = new SqlFilterParser<T>(
                tokens,
                (item, fieldName) =>
                {
                    bool found = resolveField(item, fieldName, out FilterValue value);
                    return (found, value);
                },
                isKnownField);

            try
            {
                if (parser.Match(SqlTokenKind.Where))
                {
                    // Optional SQL affordance only; expression parsing starts after WHERE.
                }

                SqlExpression expression = parser.ParseOr();
                parser.Expect(SqlTokenKind.End, "Unexpected token after expression.");
                return EtwCompiledFilter<T>.Valid(item => expression.Evaluate(item, parser._resolveField));
            }
            catch (SqlParseException ex)
            {
                return EtwCompiledFilter<T>.Invalid(ex.Message);
            }
        }

        private SqlExpression ParseOr()
        {
            SqlExpression expression = ParseAnd();
            while (Match(SqlTokenKind.Or))
            {
                SqlExpression right = ParseAnd();
                expression = new BinaryBooleanExpression(expression, right, isAnd: false);
            }

            return expression;
        }

        private SqlExpression ParseAnd()
        {
            SqlExpression expression = ParseNot();
            while (Match(SqlTokenKind.And))
            {
                SqlExpression right = ParseNot();
                expression = new BinaryBooleanExpression(expression, right, isAnd: true);
            }

            return expression;
        }

        private SqlExpression ParseNot()
        {
            if (Match(SqlTokenKind.Not))
            {
                return new NotExpression(ParseNot());
            }

            return ParsePrimary();
        }

        private SqlExpression ParsePrimary()
        {
            if (Match(SqlTokenKind.OpenParen))
            {
                SqlExpression expression = ParseOr();
                Expect(SqlTokenKind.CloseParen, "Expected closing parenthesis.");
                return expression;
            }

            SqlToken field = Expect(SqlTokenKind.Identifier, "Expected a field name.");
            if (!_isKnownField(field.Text))
            {
                throw Error(field, $"Unknown field '{field.Text}'.");
            }

            SqlToken op = Current;
            if (!IsComparisonOperator(op.Kind))
            {
                throw Error(op, "Expected a comparison operator.");
            }

            _position++;
            SqlToken literal = Current;
            if (literal.Kind is not (SqlTokenKind.String or SqlTokenKind.Number or SqlTokenKind.Identifier))
            {
                throw Error(literal, "Expected a string or numeric literal.");
            }

            _position++;
            return new ComparisonExpression(field.Text, op.Kind, literal.Text, literal.Kind == SqlTokenKind.Number);
        }

        private static bool IsComparisonOperator(SqlTokenKind kind)
        {
            return kind is SqlTokenKind.Equal or SqlTokenKind.NotEqual or SqlTokenKind.Less or
                SqlTokenKind.LessOrEqual or SqlTokenKind.Greater or SqlTokenKind.GreaterOrEqual or SqlTokenKind.Like;
        }

        private SqlToken Current => _tokens[_position];

        private bool Match(SqlTokenKind kind)
        {
            if (Current.Kind != kind)
            {
                return false;
            }

            _position++;
            return true;
        }

        private SqlToken Expect(SqlTokenKind kind, string message)
        {
            if (Current.Kind == kind)
            {
                return _tokens[_position++];
            }

            throw Error(Current, message);
        }

        private static SqlParseException Error(SqlToken token, string message)
        {
            return new SqlParseException($"{message} Position {token.Position}.");
        }

        private static bool TryTokenize(
            string text,
            out IReadOnlyList<SqlToken> tokens,
            out string errorMessage)
        {
            var result = new List<SqlToken>();
            int position = 0;
            while (position < text.Length)
            {
                char current = text[position];
                if (char.IsWhiteSpace(current))
                {
                    position++;
                    continue;
                }

                if (current == '(')
                {
                    result.Add(new SqlToken(SqlTokenKind.OpenParen, "(", position));
                    position++;
                    continue;
                }

                if (current == ')')
                {
                    result.Add(new SqlToken(SqlTokenKind.CloseParen, ")", position));
                    position++;
                    continue;
                }

                if (current == '\'')
                {
                    if (!TryReadString(text, position, out string value, out int nextPosition))
                    {
                        tokens = Array.Empty<SqlToken>();
                        errorMessage = $"Unterminated string literal. Position {position}.";
                        return false;
                    }

                    result.Add(new SqlToken(SqlTokenKind.String, value, position));
                    position = nextPosition;
                    continue;
                }

                if (char.IsDigit(current))
                {
                    int start = position;
                    position++;
                    while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.'))
                    {
                        position++;
                    }

                    result.Add(new SqlToken(SqlTokenKind.Number, text[start..position], start));
                    continue;
                }

                if (IsIdentifierStart(current))
                {
                    int start = position;
                    position++;
                    while (position < text.Length && IsIdentifierPart(text[position]))
                    {
                        position++;
                    }

                    string value = text[start..position];
                    result.Add(new SqlToken(GetIdentifierKind(value), value, start));
                    continue;
                }

                SqlTokenKind? operatorKind = current switch
                {
                    '=' => SqlTokenKind.Equal,
                    '<' when position + 1 < text.Length && text[position + 1] == '=' => SqlTokenKind.LessOrEqual,
                    '<' when position + 1 < text.Length && text[position + 1] == '>' => SqlTokenKind.NotEqual,
                    '<' => SqlTokenKind.Less,
                    '>' when position + 1 < text.Length && text[position + 1] == '=' => SqlTokenKind.GreaterOrEqual,
                    '>' => SqlTokenKind.Greater,
                    '!' when position + 1 < text.Length && text[position + 1] == '=' => SqlTokenKind.NotEqual,
                    _ => null
                };

                if (operatorKind is null)
                {
                    tokens = Array.Empty<SqlToken>();
                    errorMessage = $"Unexpected character '{current}'. Position {position}.";
                    return false;
                }

                result.Add(new SqlToken(operatorKind.Value, current.ToString(), position));
                position += operatorKind is SqlTokenKind.LessOrEqual or SqlTokenKind.GreaterOrEqual or SqlTokenKind.NotEqual ? 2 : 1;
            }

            result.Add(new SqlToken(SqlTokenKind.End, string.Empty, position));
            tokens = result;
            errorMessage = string.Empty;
            return true;
        }

        private static bool TryReadString(string text, int start, out string value, out int nextPosition)
        {
            var builder = new StringBuilder();
            int position = start + 1;
            while (position < text.Length)
            {
                char current = text[position];
                if (current == '\'')
                {
                    if (position + 1 < text.Length && text[position + 1] == '\'')
                    {
                        builder.Append('\'');
                        position += 2;
                        continue;
                    }

                    value = builder.ToString();
                    nextPosition = position + 1;
                    return true;
                }

                builder.Append(current);
                position++;
            }

            value = string.Empty;
            nextPosition = position;
            return false;
        }

        private static bool IsIdentifierStart(char value)
        {
            return char.IsLetter(value) || value == '_';
        }

        private static bool IsIdentifierPart(char value)
        {
            return char.IsLetterOrDigit(value) || value is '_' or '-' or '.';
        }

        private static SqlTokenKind GetIdentifierKind(string value)
        {
            return value.ToUpperInvariant() switch
            {
                "AND" => SqlTokenKind.And,
                "OR" => SqlTokenKind.Or,
                "NOT" => SqlTokenKind.Not,
                "LIKE" => SqlTokenKind.Like,
                "WHERE" => SqlTokenKind.Where,
                _ => SqlTokenKind.Identifier
            };
        }

        private abstract class SqlExpression
        {
            public abstract bool Evaluate(T item, Func<T, string, (bool Found, FilterValue Value)> resolveField);
        }

        private sealed class BinaryBooleanExpression : SqlExpression
        {
            private readonly SqlExpression _left;
            private readonly SqlExpression _right;
            private readonly bool _isAnd;

            public BinaryBooleanExpression(SqlExpression left, SqlExpression right, bool isAnd)
            {
                _left = left;
                _right = right;
                _isAnd = isAnd;
            }

            public override bool Evaluate(T item, Func<T, string, (bool Found, FilterValue Value)> resolveField)
            {
                return _isAnd
                    ? _left.Evaluate(item, resolveField) && _right.Evaluate(item, resolveField)
                    : _left.Evaluate(item, resolveField) || _right.Evaluate(item, resolveField);
            }
        }

        private sealed class NotExpression : SqlExpression
        {
            private readonly SqlExpression _inner;

            public NotExpression(SqlExpression inner)
            {
                _inner = inner;
            }

            public override bool Evaluate(T item, Func<T, string, (bool Found, FilterValue Value)> resolveField)
            {
                return !_inner.Evaluate(item, resolveField);
            }
        }

        private sealed class ComparisonExpression : SqlExpression
        {
            private readonly string _fieldName;
            private readonly SqlTokenKind _operatorKind;
            private readonly string _literal;
            private readonly bool _literalIsNumber;

            public ComparisonExpression(string fieldName, SqlTokenKind operatorKind, string literal, bool literalIsNumber)
            {
                _fieldName = fieldName;
                _operatorKind = operatorKind;
                _literal = literal;
                _literalIsNumber = literalIsNumber;
            }

            public override bool Evaluate(T item, Func<T, string, (bool Found, FilterValue Value)> resolveField)
            {
                (bool found, FilterValue value) = resolveField(item, _fieldName);
                if (!found)
                {
                    return false;
                }

                if (_operatorKind == SqlTokenKind.Like)
                {
                    return WildcardMatcher.IsMatch(value.Text, _literal, appWildcardsOnly: false);
                }

                if (TryCompareNumbers(value, out int numericComparison))
                {
                    return Compare(numericComparison);
                }

                int textComparison = string.Compare(value.Text, _literal, StringComparison.CurrentCultureIgnoreCase);
                return Compare(textComparison);
            }

            private bool TryCompareNumbers(FilterValue value, out int comparison)
            {
                comparison = 0;
                if (value.Number is null || !_literalIsNumber)
                {
                    return false;
                }

                if (!decimal.TryParse(_literal, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal literalNumber))
                {
                    return false;
                }

                comparison = value.Number.Value.CompareTo(literalNumber);
                return true;
            }

            private bool Compare(int comparison)
            {
                return _operatorKind switch
                {
                    SqlTokenKind.Equal => comparison == 0,
                    SqlTokenKind.NotEqual => comparison != 0,
                    SqlTokenKind.Less => comparison < 0,
                    SqlTokenKind.LessOrEqual => comparison <= 0,
                    SqlTokenKind.Greater => comparison > 0,
                    SqlTokenKind.GreaterOrEqual => comparison >= 0,
                    _ => false
                };
            }
        }
    }

    private sealed class SqlParseException : Exception
    {
        public SqlParseException(string message)
            : base(message)
        {
        }
    }

    private static class WildcardMatcher
    {
        public static bool IsMatch(string value, string pattern, bool appWildcardsOnly)
        {
            string regexPattern = CreateRegexPattern(pattern, appWildcardsOnly);
            return Regex.IsMatch(
                value,
                regexPattern,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100));
        }

        private static string CreateRegexPattern(string pattern, bool appWildcardsOnly)
        {
            var builder = new StringBuilder("^");
            foreach (char character in pattern)
            {
                switch (character)
                {
                    case '*':
                        builder.Append(".*");
                        break;
                    case '?':
                        builder.Append('.');
                        break;
                    case '%' when !appWildcardsOnly:
                        builder.Append(".*");
                        break;
                    case '_' when !appWildcardsOnly:
                        builder.Append('.');
                        break;
                    default:
                        builder.Append(Regex.Escape(character.ToString()));
                        break;
                }
            }

            builder.Append('$');
            return builder.ToString();
        }
    }
}
