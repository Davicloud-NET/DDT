namespace DDT.Agent.Deployment;

// The UEFI global variables, such as BootOrder and Boot####.
public interface IUefiVariables
{
    // Null when the variable does not exist.
    byte[]? Read(string name);

    void Write(string name, byte[] value);

    // Nothing happens when the variable does not exist.
    void Delete(string name);
}
