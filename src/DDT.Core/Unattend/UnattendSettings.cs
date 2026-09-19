namespace DDT.Core.Unattend;

public sealed record UnattendSettings(
    string ProcessorArchitecture,
    string ComputerName,
    string? TimeZone,
    string UiLanguage,
    string Locale,
    string Keyboard,
    LocalAdministrator? LocalAdministrator,
    DomainJoin? DomainJoin);
