using System.Configuration;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Dotcss;

public enum TokenType : byte
{
    Unknown,
	Ident,
	AtKeyword,
	String,
	BadString,
	UnquotedString,
	Hash,
	Num,
	Percentage,
	Dimension,
	UnicodeRange,
	CDO, // <!--
	CDC, // -->
	Colon,
	SemiColon,
	CurlyL,
	CurlyR,
	ParenthesisL,
	ParenthesisR,
	BracketL,
	BracketR,
	Whitespace,
	Includes,
	Dashmatch, // |=
	SubstringOperator, // *=
	PrefixOperator, // ^=
	SuffixOperator, // $=
	Delim,
	EMS, // 3em
	EXS, // 3ex
	Length,
	Angle,
	Time,
	Freq,
	Exclamation,
	Resolution,
	Comma,
	Charset,
	EscapedJavaScript,
	BadEscapedJavaScript,
	Comment,
	SingleLineComment,
	EOF,

    Extension = byte.MaxValue
}


[StructLayout(LayoutKind.Auto)]
public readonly record struct Token
{
    internal readonly object? _value;
    public readonly TokenType Type;

    public readonly int Start; // Range[0]
    public readonly int End; // Range[1]
    public readonly int LineNumber;
    public readonly int LineStart;

    public object? Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return Type is not TokenType.Extension ? _value : GetValueFromHolder(in this);

            [MethodImpl(MethodImplOptions.NoInlining)]
            static object? GetValueFromHolder(in Token token) => ((ValueHolder) token._value!).Value;
        }
    }


    internal Token(
        TokenType type,
        object? value,
        int start,
        int end,
        int lineNumber,
        int lineStart)
    {
        Type = type;
        _value = value;

        Start = start;
        End = end;
        LineNumber = lineNumber;
        LineStart = lineStart;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Token Create(TokenType type, object? value, int start, int end, int lineNumber, int lineStart)
    {
        return new Token(type, value, start, end, lineNumber, lineStart);
    }

    internal abstract record ValueHolder(object? Value);
}