using Hack13.Contracts.Protocol;
using Hack13.Contracts.ScreenCatalog;
using Hack13.TerminalServer.Protocol;

namespace Hack13.TerminalServer.Engine;

/// <summary>
/// Renders a screen definition with dynamic data into a 5250 data stream record.
/// </summary>
public class ScreenRenderer
{
    /// <summary>
    /// Renders a complete screen (clear + write) with data substitution.
    /// Field values in the data dictionary are substituted into display fields
    /// using the field Name as the key.
    /// </summary>
    public byte[] RenderScreen(ScreenDefinition screen, Dictionary<string, string> data, string? errorMessage = null)
    {
        var writer = new DataStreamWriter();

        // Clear screen first
        writer.ClearUnit();

        // Start Write to Display
        writer.WriteToDisplay(Tn5250Constants.CC1_LOCK_KEYBOARD, Tn5250Constants.CC2_NO_FLAGS);

        // Write static text elements
        foreach (var text in screen.StaticText.OrderBy(t => t.Row).ThenBy(t => t.Col))
        {
            writer.SetBufferAddress(text.Row, text.Col);
            writer.WriteText(text.Text);
        }

        // Write fields
        FieldDefinition? firstInputField = null;

        foreach (var field in screen.Fields.OrderBy(f => f.Row).ThenBy(f => f.Col))
        {
            // Position at the field's attribute byte location (one before the field data)
            writer.SetBufferAddress(field.Row, field.Col);

            if (field.Type == "input")
            {
                firstInputField ??= field;

                // Write field start order
                if (field.Attributes == "hidden")
                    writer.StartHiddenField();
                else
                    writer.StartInputField();

                // Write the field value or default, padded to field length
                var value = data.TryGetValue(field.Name, out var v) ? v : field.DefaultValue;
                writer.WriteFieldValue(value, field.Length);

                // End-of-field attribute (protected) to mark field boundary
                writer.StartProtectedField();
            }
            else // "display"
            {
                writer.StartProtectedField();
                var value = data.TryGetValue(field.Name, out var v) ? v : field.DefaultValue;

                if (field.Attributes == "highlighted")
                {
                    // For highlighted fields, just write the value
                    // (real 5250 would use WEA for color/highlight attributes)
                    writer.WriteFieldValue(value, field.Length);
                }
                else
                {
                    writer.WriteFieldValue(value, field.Length);
                }
            }
        }

        // Write error message on line 24 if present
        if (!string.IsNullOrEmpty(errorMessage))
        {
            writer.SetBufferAddress(24, 2);
            writer.StartProtectedField();
            writer.WriteFieldValue(errorMessage, 78);
        }

        // Place cursor at first input field (after the SF attribute byte)
        if (firstInputField != null)
        {
            writer.SetBufferAddress(firstInputField.Row, firstInputField.Col + 1);
            writer.InsertCursor();
        }

        return writer.Build();
    }
}
