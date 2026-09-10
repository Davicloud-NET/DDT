namespace DDT.Protocols.Dhcp;

public enum DhcpParseError
{
    None,
    TooShort,
    BadMagicCookie,
    OptionOverrunsDatagram,
    MissingMessageType,
}
