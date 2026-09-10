using System.Text;

namespace DDT.Server.Ldap;

public static class LdapFilter
{
    public static string EscapeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '*': builder.Append("\\2a"); break;
                case '(': builder.Append("\\28"); break;
                case ')': builder.Append("\\29"); break;
                case '\\': builder.Append("\\5c"); break;
                case '\0': builder.Append("\\00"); break;
                case '/': builder.Append("\\2f"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    public static string EscapeDistinguishedNameValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            if (c is ',' or '\\' or '#' or '+' or '<' or '>' or ';' or '"' or '=')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
