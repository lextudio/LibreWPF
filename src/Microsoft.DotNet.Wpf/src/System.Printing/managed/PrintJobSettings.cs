namespace System.Printing;

/// <summary>
/// The description and ticket a queue applies to the next job. <c>PrintDialog</c> sets <see cref="Description"/> before it
/// creates the writer, and the queue printer uses it as the backend's job name.
/// </summary>
public partial class PrintJobSettings
{
    /// <summary>
    /// The ticket the next job uses. Null until a caller sets one; the queue's own defaults apply then.
    /// </summary>
    public PrintTicket CurrentPrintTicket { get; set; } = null!;

    public string Description { get; set; } = string.Empty;
}
