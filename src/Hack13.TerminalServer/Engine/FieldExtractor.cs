using Hack13.Contracts.Protocol;
using Hack13.Contracts.ScreenCatalog;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Engine;

/// <summary>
/// Extracts named field values from a parsed input record by matching
/// field positions against the screen definition.
/// </summary>
public class FieldExtractor
{
    /// <summary>
    /// Maps the modified fields from an input record to named values
    /// using the screen definition's field positions.
    /// Returns a dictionary of field_name → value.
    /// </summary>
    public Dictionary<string, string> Extract(InputRecord input, ScreenDefinition screen)
    {
        var result = new Dictionary<string, string>();

        foreach (var modifiedField in input.Fields)
        {
            // Find the screen field definition that matches this position.
            // The modified field's SBA position points to the start of the field data,
            // which is at (field.Col + 1) because field.Col is where the SF attribute byte sits.
            var fieldDef = screen.Fields.FirstOrDefault(f =>
                f.Type == "input" &&
                f.Row == modifiedField.Row &&
                (modifiedField.Col == f.Col + 1 || modifiedField.Col == f.Col));

            if (fieldDef != null)
            {
                result[fieldDef.Name] = modifiedField.Value;
            }
        }

        return result;
    }
}
