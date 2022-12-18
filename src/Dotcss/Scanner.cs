using System.Data;
using System.Runtime.CompilerServices;
using static Dotcss.ExceptionHelper;

namespace Dotcss;

internal readonly struct ScannerState
{
    public readonly int Index;
    public readonly int LineNumber;
    public readonly int LineStart;
    public readonly ArrayList<string> CurlyStack;

    public ScannerState(int index, int lineNumber, int lineStart, ArrayList<string> curlyStack)
    {
        Index = index;
        LineNumber = lineNumber;
        LineStart = lineStart;
        CurlyStack = curlyStack;
    }
}

public sealed class MultiLineStream
{
    private readonly ReadOnlyMemory<char> _source;
    private int position;

    private int length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _source.Length;
    }

    public MultiLineStream(string source)
    {
        _source = source.AsMemory();
        position = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyMemory<char> Substring(int from, int to)
    {
        return to > _source.Length ? ReadOnlyMemory<char>.Empty : _source.Slice(from, to - from);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyMemory<char> Substring(int from)
    {
        return Substring(from, position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EOS()
    {
        return length <= position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Position()
    {
        return position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GoBackTo(int position)
    {
        this.position = position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GoBack(int step)
    {
        position -= step;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int step)
    {
        position += step;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char NextChar()
    {
        var span = Substring(position++, position + 1);
        return span.IsEmpty ? '\0' : span.Span[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char PeekChar(int step = 0)
    {
        var offset = position + step;
        var span = Substring(offset, offset + 1);
        return span.IsEmpty ? '\0' : span.Span[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char LookbackChar(int step = 0)
    {
        var offset = position - step;
        var span = Substring(offset, offset + 1);
        return span.IsEmpty ? '\0' : span.Span[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AdvanceIfChar(char @char)
    {
        if (!@char.Equals(PeekChar())) return false;
        Advance(1);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AdvanceIfChars(ReadOnlySpan<char> chars)
    {
        if (position + chars.Length > length)
        {
            return false;
        }

        var i = 0;
        for (; i < chars.Length; i++)
        {
            if (!PeekChar(i).Equals(chars[i]))
            {
                return false;
            }
        }

        Advance(i);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AdvanceWhileChar(Func<char, bool> condition)
    {
        var posNow = position;
        while (position < length && condition(PeekChar()))
        {
            Advance(1);
        }

        return position - posNow;
    }
}

public partial class Scanner
{
    private MultiLineStream stream;
    private bool ignoreComment = false;

    private bool ignoreWhitespace = true;
    //private bool inURL = false;

    public Scanner(string source)
    {
        stream = new(source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Token ScanUnquotedString()
    {
        var from = stream.Position();
        var readCount = stream.AdvanceWhileChar(IsUnquotedChar);
        var to = from + readCount;
        if (readCount > 0)
        {
            return new Token(TokenType.UnquotedString, stream.Substring(from), from, to, 0, 0);
        }

        return ThrowArgumentNullException<Token>("No string red");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsUnquotedChar(char ch)
        {
            return ch is not ('\\' or '\'' or '"' or '(' or ')' or ' ' or '\t' or '\n' or '\f' or '\r');
        }
    }

    public Token Scan()
    {
        var triviaToken = Trivia();
        if (triviaToken is not null)
        {
            return triviaToken.Value;
        }

        var offset = stream.Position();
        if (stream.EOS())
        {
            return Token.Create(TokenType.EOF, null, offset, offset, 0, 0);
        }

        return ScanNext(offset);
    }

    protected Token ScanNext(int offset)
    {
        if (stream.AdvanceIfChars("<!--".AsSpan()))
        {
            return Token.Create(TokenType.CDO, null, offset, offset + 4, 0, 0);
        }

        if (stream.AdvanceIfChars("-->".AsSpan()))
        {
            return Token.Create(TokenType.CDC, null, offset, offset + 3, 0, 0);
        }

        var identity = ScanIdentity();
        if (!identity.IsEmpty)
        {
            return new Token(TokenType.Ident, identity.ToString(), offset, stream.Position(), 0, 0);
        }

        if (stream.AdvanceIfChar('@'))
        {
            var name = ScanName();
            if (name.IsEmpty)
            {
                return Token.Create(TokenType.Delim, null, offset, offset, 0, 0);
            }

            var atToken = TokenType.AtKeyword;
            if (name.Equals("charset".AsSpan(), StringComparison.InvariantCultureIgnoreCase))
            {
                atToken = TokenType.Charset;
            }

            return Token.Create(atToken, stream.Substring(offset), offset, stream.Position(), 0, 0);
        }

        if (stream.AdvanceIfChar('#'))
        {
            var name = ScanName();
            if (name.IsEmpty)
            {
                return Token.Create(TokenType.Delim, null, offset, stream.Position(), 0, 0);
            }

            return Token.Create(TokenType.Hash, name.ToString(), offset, stream.Position(), 0, 0);
        }

        if (stream.AdvanceIfChar('!'))
        {
            return Token.Create(TokenType.Exclamation, null, offset, offset + 1, 0, 0);
        }

        if (!ScanNumber().IsEmpty)
        {
            var pos = stream.Position();
            if (stream.AdvanceIfChar('%'))
            {
                return Token.Create(TokenType.Percentage, null, offset, stream.Position(), 0, 0);
            }

            if (!ScanIdentity().IsEmpty)
            {
                var dimention = stream.Substring(pos).Span.ToString().ToLowerInvariant();
                var dimentionToken = dimention switch
                {
                    "em" => TokenType.EMS,
                    "ex" => TokenType.EXS,
                    "px" => TokenType.Length,
                    "cm" => TokenType.Length,
                    "mm" => TokenType.Length,
                    "in" => TokenType.Length,
                    "pt" => TokenType.Length,
                    "pc" => TokenType.Length,
                    "dec" => TokenType.Angle,
                    "rad" => TokenType.Angle,
                    "grad" => TokenType.Angle,
                    "ms" => TokenType.Time,
                    "s" => TokenType.Time,
                    "hz" => TokenType.Freq,
                    "khz" => TokenType.Freq,
                    "%" => TokenType.Percentage,
                    "fr" => TokenType.Percentage,
                    "dpi" => TokenType.Resolution,
                    "dpcm" => TokenType.Resolution,
                    _ => TokenType.Dimension
                };
                return Token.Create(dimentionToken, stream.Substring(offset), offset, stream.Position(), 0, 0);
            }

            return new Token(TokenType.Num, null, offset, stream.Position(), 0, 0);
        }

        var asString = ScanString();
        if (!asString.IsEmpty)
        {
            return Token.Create(TokenType.String, asString.ToString(), offset, stream.Position(), 0, 0);
        }

        var asToken = stream.PeekChar() switch
        {
            ';' => TokenType.SemiColon,
            ':' => TokenType.Colon,
            '{' => TokenType.CurlyL,
            '}' => TokenType.CurlyR,
            '[' => TokenType.BracketL,
            ']' => TokenType.BracketR,
            '(' => TokenType.ParenthesisL,
            ')' => TokenType.ParenthesisR,
            ',' => TokenType.Comma,
            _ => TokenType.Unknown
        };
        if (asToken is not TokenType.Unknown)
        {
            stream.Advance(1);
            return Token.Create(asToken, null, offset, offset + 1, 0, 0);
        }

        var operators = stream.Substring(offset, offset + 2);
        if (operators.Span.Equals("~=".AsSpan(), StringComparison.Ordinal))
        {
            stream.Advance(2);
            return Token.Create(TokenType.Includes, operators, offset, offset + 2, 0, 0);
        }

        if (operators.Span.Equals("|=".AsSpan(), StringComparison.Ordinal))
        {
            stream.Advance(2);
            return Token.Create(TokenType.Dashmatch, operators, offset, offset + 2, 0, 0);
        }

        if (operators.Span.Equals("*=".AsSpan(), StringComparison.Ordinal))
        {
            stream.Advance(2);
            return Token.Create(TokenType.SubstringOperator, operators, offset, offset + 2, 0, 0);
        }

        if (operators.Span.Equals("^=".AsSpan(), StringComparison.Ordinal))
        {
            stream.Advance(2);
            return Token.Create(TokenType.PrefixOperator, operators, offset, offset + 2, 0, 0);
        }

        if (operators.Span.Equals("$=".AsSpan(), StringComparison.Ordinal))
        {
            stream.Advance(2);
            return Token.Create(TokenType.SuffixOperator, operators, offset, offset + 2, 0, 0);
        }

        stream.NextChar();
        return Token.Create(TokenType.Delim, null, offset, offset + 1, 0, 0);
    }

    public ReadOnlySpan<char> ScanIdentity()
    {
        var from = stream.Position();
        var isMinus = stream.PeekChar();
        if (isMinus is '-')
        {
            stream.Advance(1);
            isMinus = stream.PeekChar();
            if (isMinus is '-' || !ScanIdentFirstChar().IsEmpty || !ScanEscape(false).IsEmpty)
            {
                while (!ScanIdentChar().IsEmpty || !ScanEscape(false).IsEmpty)
                {
                }

                return stream.Substring(from).Span;
            }
        }
        else if (!ScanIdentFirstChar().IsEmpty || !ScanEscape(false).IsEmpty)
        {
            while (!ScanIdentChar().IsEmpty || !ScanEscape(false).IsEmpty)
            {
            }

            return stream.Substring(from).Span;
        }

        stream.GoBackTo(from);
        return ReadOnlySpan<char>.Empty;
    }

    public ReadOnlySpan<char> ScanEscape(bool includeNewLines)
    {
        var @char = stream.PeekChar();
        if (!'\\'.Equals(@char))
        {
            return ReadOnlySpan<char>.Empty;
        }

        stream.Advance(1);

        var from = stream.Position();
        var hexNumCount = stream.AdvanceWhileChar(IsHexChar);

        if (hexNumCount > 0)
        {
            var hexValue = stream.Substring(from);
            @char = stream.PeekChar();
            if (@char is (' ' or '\t'))
            {
                stream.Advance(1);
            }
            else
            {
                ScanNewline();
            }

            return hexValue.Span;
        }

        var ch = stream.PeekChar();
        if (ch is not ('\r' or '\f' or '\n'))
        {
            stream.Advance(1);
        }
        else if (includeNewLines)
        {
            ScanNewline();
        }

        return stream.Substring(from).Span;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsHexChar(char ch)
        {
            return ch is (>= 'a' and <= 'f') or (>= 'A' and <= 'F') or (>= '0' and <= '9');
        }
    }

    public ReadOnlySpan<char> ScanName()
    {
        var from = stream.Position();
        while (!ScanIdentChar().IsEmpty || !ScanEscape(false).IsEmpty)
        {
            
        }
        return stream.Substring(from).Span;
    }

    public bool ScanNewline()
    {
        var from = stream.Position();
        stream.AdvanceWhileChar(IsNewLine);
        stream.AdvanceIfChars("\r\n".AsSpan());
        return !stream.Substring(from).IsEmpty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsNewLine(char ch)
        {
            return ch is ('\n' or '\f' or '\r');
        }
    }

    public ReadOnlySpan<char> ScanNumber()
    {
        var from = stream.Position();
        var npeek = 0;
        var ch = stream.PeekChar();
        if (ch is '.')
        {
            npeek = 1;
        }

        ch = stream.PeekChar(npeek);
        if (ch is (>= '0' and <= '9'))
        {
            stream.Advance(npeek + 1);
            stream.AdvanceWhileChar(IsNumeric);
            return stream.Substring(from).Span;
        }

        return ReadOnlySpan<char>.Empty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsNumeric(char ch)
        {
            return (ch is (>= '0' and <= '9')) || npeek == 0 && ch is '.';
        }
    }

    public Token? Trivia()
    {
        while (true)
        {
            var offset = stream.Position();
            if (!ScanWhitespace().IsEmpty)
            {
                if (!ignoreWhitespace)
                {
                    return Token.Create(TokenType.Whitespace, stream.Substring(offset), offset, stream.Position(), 0,
                        0);
                }
            }
            else if (!ScanComment().IsEmpty)
            {
                if (!ignoreComment)
                {
                    return Token.Create(TokenType.Comment, stream.Substring(offset), offset, stream.Position(), 0, 0);
                }
            }
            else
            {
                return null;
            }
        }
    }

    public ReadOnlySpan<char> ScanWhitespace()
    {
        var from = stream.Position();
        _ = stream.AdvanceWhileChar(IsWhitespaceChar);
        return stream.Substring(from).Span;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsWhitespaceChar(char ch)
        {
            return ch is ' ' or '\t' or '\n' or '\f' or '\r';
        }
    }

    public ReadOnlySpan<char> ScanComment()
    {
        if (!stream.AdvanceIfChars("/*".AsSpan()))
        {
            return ReadOnlySpan<char>.Empty;
        }

        var from = stream.Position();
        var success = false;
        var hot = false;
        stream.AdvanceWhileChar(IsEndofComment);
        if (success)
        {
            stream.Advance(1);
        }

        return stream.Substring(from).Span;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsEndofComment(char ch)
        {
            if (hot && ch == '/')
            {
                success = true;
                return false;
            }

            hot = ch == '*';
            return true;
        }
    }

    public ReadOnlySpan<char> ScanString()
    {
        var ch = stream.PeekChar();
        if (ch is not ('\'' or '"'))
        {
            return ReadOnlySpan<char>.Empty;
        }

        var from = stream.Position();
        var closeQuote = stream.NextChar();
        stream.AdvanceWhileChar(ContinueEndOfString);
        if (stream.PeekChar() != closeQuote)
        {
            return ReadOnlySpan<char>.Empty;
        }

        stream.Advance(1);
        return stream.Substring(from).Span;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool ContinueEndOfString(char ch)
        {
            if (ch != closeQuote && ch is not ('\\' or '\n' or '\f' or '\r'))
            {
                return true;
            }

            return false;
        }
    }

    public ReadOnlySpan<char> ScanIdentChar()
    {
        var from = stream.Position();
        var ch = stream.PeekChar();
        if (ch is ('_' or '-' or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
            or (>= (char)0x80 and <= (char)0xFFFF)))
        {
            stream.Advance(1);
            return stream.Substring(from).Span;
        }

        return ReadOnlySpan<char>.Empty;
    }

    public ReadOnlySpan<char> ScanIdentFirstChar()
    {
        var from = stream.Position();
        var ch = stream.PeekChar();
        if (ch is ('_' or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= (char)0x80 and <= (char)0xFFFF)))
        {
            stream.Advance(1);
            return stream.Substring(from).Span;
        }

        return ReadOnlySpan<char>.Empty;
    }

    public ReadOnlySpan<char> TryScanUnicode()
    {
        var pos = stream.Position();
        if (!stream.EOS() && stream.AdvanceIfChar('+'))
        {
            var codePoints = stream.AdvanceWhileChar(IsHexDigit) + stream.AdvanceWhileChar(IsQuestionChar);
            if (codePoints is >= 1 and <= 6)
            {
                if (stream.AdvanceIfChar('-'))
                {
                    var digits = stream.AdvanceWhileChar(IsHexDigit);
                    if (digits is >= 1 and <= 6)
                    {
                        return stream.Substring(pos).Span;
                    }
                }
                else
                {
                    return stream.Substring(pos).Span;
                }
            }
        }

        stream.GoBackTo(pos);
        return ReadOnlySpan<char>.Empty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsHexDigit(char ch)
        {
            return ch is (>= 'a' and <= 'f') or (>= 'A' and <= 'F') or (>= '0' and <= '9');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsQuestionChar(char ch)
        {
            return ch is '?';
        }
    }
}