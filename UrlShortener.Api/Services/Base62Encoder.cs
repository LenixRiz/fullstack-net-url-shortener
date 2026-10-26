namespace UrlShortener.Api.Services;

public static class Base62Encoder
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly int Base = Alphabet.Length;

    public static string Encode(long number)
    {
        if (number == 0) return Alphabet[0].ToString();

        Span<char> buffer = stackalloc char[11];
        int index = buffer.Length;

        while (number > 0)
        {
            buffer[--index] = Alphabet[(int)(number % Base)];
            number /= Base;
        }

        return new string(buffer[index..]);
    }
}