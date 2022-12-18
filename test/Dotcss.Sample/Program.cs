using Dotcss;


//AssertTokenSequence(" @", TokenType.Delim);
//AssertTokenSequence(" /* comment*/ \n/*comment*/@", TokenType.Comment, TokenType.Comment, TokenType.Delim);
//AssertTokenSequence("\u060frf", TokenType.Ident);
//AssertTokenSequence("über", TokenType.Ident);
//AssertTokenSequence("red-->", TokenType.Ident, TokenType.Delim);
//AssertTokenSequence("@import", TokenType.AtKeyword);
//AssertTokenSequence("#import", TokenType.Hash);
//AssertTokenSequence("@charset", TokenType.Charset);
//AssertTokenSequence("\\E9motion", TokenType.Ident);

var content = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Test.css"));
//content = "background: ident 1.2e+3 100% 4em/5ex calc(1em + 3% * 5)";
//content = "u+123-456";
Scanner scanner = new Scanner(content);
/*
Ident    background
Colon    :
Ident    ident
Dimension        1.2e
Delim    +
Num      3
Percentage       100%
EMS      4em
Delim    /
EXS      5ex
Ident    calc
ParenthesisL     (
EMS      1em
Delim    +
Percentage       3%
Delim    *
Num      5
ParenthesisR     )
 */
Token token;
while ((token = scanner.Scan()).Type != TokenType.EOF)
{
    Console.WriteLine($"{token.Type} \t {content[token.Start..token.End]}");
}

void AssertTokenSequence(string source, params TokenType[] tokenTypes)
{
    Scanner scanner = new(source);
    Token token = scanner.Scan();
    List<TokenType> types = new List<TokenType>();
    do
    {
        types.Add(token.Type);
        token = scanner.Scan();
    } while (token.Type != TokenType.EOF);

    if (!types.SequenceEqual(tokenTypes))
    {
        Console.Write(source);
        Console.Write("\n\tActual : \t");
        foreach (var type in types)
        {
            Console.Write(type);
            Console.Write(", ");
        }

        Console.Write("\n\tExpected : \t");
        foreach (var type in tokenTypes)
        {
            Console.Write(type);
            Console.Write(", ");
        }

        Console.WriteLine();
    }
}

