namespace IMCAPI.Services;

/// <summary>
/// The file arrived and was readable, but it holds nothing to import: an empty CSV, or one that is
/// only a header. It is the user's file, not a server failure, so the controller answers 400 with the
/// reason instead of an empty tree that looks like a successful import of zero records.
/// </summary>
public sealed class ImportFileException : Exception
{
    public ImportFileException(string message) : base(message)
    {
    }
}
