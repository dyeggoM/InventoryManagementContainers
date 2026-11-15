namespace IMCAPI.Services;

/// <summary>
/// Which reader the importer should use. The controller knows the extension of the file it received,
/// so it says which format this is instead of the importer sniffing bytes: a CSV that happens to look
/// like a zip is still a CSV, and a workbook is never guessed at.
/// </summary>
public enum ImportFormat
{
    /// <summary>An .xlsx or .xls workbook, read sheet by sheet.</summary>
    Workbook,

    /// <summary>A .csv file: one sheet, named after the file it came from.</summary>
    Csv,
}
