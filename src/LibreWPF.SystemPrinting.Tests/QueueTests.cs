using System.Printing;
using PrintingTools.Core;
using Xunit;
using CoreCapabilities = PrintingTools.Core.PrintCapabilities;

namespace LibreWPF.SystemPrinting.Tests;

public class QueueTests
{
    private sealed class FakeAdapter : IPrintAdapter
    {
        public IReadOnlyList<PrinterInfo> Printers { get; init; } = Array.Empty<PrinterInfo>();

        public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default) => Task.FromResult(Printers);

        public Task<CoreCapabilities> GetCapabilitiesAsync(PrinterId printerId, PrintTicketModel? baseTicket = null, CancellationToken cancellationToken = default) => Task.FromResult(CoreCapabilities.CreateDefault());

        public Task<PrintSession> CreateSessionAsync(PrintRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task PrintAsync(PrintSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PrintPreviewModel> CreatePreviewAsync(PrintSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static PrinterInfo Info(string id, string name, bool isDefault = false, bool online = true, params (string Key, string Value)[] attributes) =>
        new(new PrinterId(id), name, isDefault, online, true, attributes.ToDictionary(a => a.Key, a => a.Value));

    private static void Use(params PrinterInfo[] printers) => PrintBackendAccess.Use(new FakeAdapter { Printers = printers });

    [Fact]
    public void EnumeratesTheBackendQueuesWithServerQualifiedNames()
    {
        Use(Info("office", "Office Laser", attributes: ("printer-location", "2nd floor")), Info("pdf", "PDF", online: false));
        var server = new LocalPrintServer();

        var queues = server.GetPrintQueues().ToList();

        Assert.Equal(new[] { "Office Laser", "PDF" }, queues.Select(q => q.Name));
        Assert.Equal("\\\\" + Environment.MachineName + "\\Office Laser", queues[0].FullName);
        Assert.Equal("2nd floor", queues[0].Location);
        Assert.False(queues[0].IsOffline);
        Assert.True(queues[1].IsOffline);
        Assert.Same(server, queues[0].HostingPrintServer);
    }

    [Fact]
    public void DefaultQueueIsTheBackendDefault()
    {
        Use(Info("a", "A"), Info("b", "B", isDefault: true));

        Assert.Equal("B", new LocalPrintServer().DefaultPrintQueue.Name);
        Assert.Equal("B", LocalPrintServer.GetDefaultPrintQueue().Name);
    }

    [Fact]
    public void NoDefaultQueueIsAnError()
    {
        Use(Info("a", "A"));
        Assert.Throws<InvalidOperationException>(() => new LocalPrintServer().DefaultPrintQueue);
    }

    [Fact]
    public void QueueCanBeOpenedByNameIdOrFullName()
    {
        Use(Info("office_laser", "Office Laser"));
        var server = new PrintServer();

        Assert.Equal("Office Laser", new PrintQueue(server, "Office Laser").Name);
        Assert.Equal("Office Laser", new PrintQueue(server, "office_laser").Name);
        Assert.Equal("Office Laser", new PrintQueue(server, server.Name + "\\Office Laser").Name);
        Assert.Throws<ArgumentException>(() => new PrintQueue(server, "missing"));
    }

    [Fact]
    public void RemoteServersAreRefused()
    {
        Assert.Throws<PlatformNotSupportedException>(() => new PrintServer("\\\\other-machine"));
        var canonical = "\\\\" + Environment.MachineName;
        Assert.Equal(canonical, new PrintServer("\\\\" + Environment.MachineName).Name);
        Assert.Equal(canonical, new PrintServer(Environment.MachineName).Name);
        Assert.Equal(canonical, new PrintServer("\\\\" + Environment.MachineName.ToLowerInvariant() + "\\").Name);
        Assert.Equal(canonical, new LocalPrintServer().Name);
    }

    [Fact]
    public void MembersThatAreNotBackedFailLoudlyInsteadOfReturningDefaults()
    {
        Use(Info("a", "A"));
        var queue = new PrintQueue(new PrintServer(), "A");

        Assert.Throws<PlatformNotSupportedException>(() => queue.IsBusy);
        Assert.Throws<PlatformNotSupportedException>(() => queue.DefaultPriority);
        Assert.Throws<PlatformNotSupportedException>(() => queue.Comment = "x");
        Assert.Throws<PlatformNotSupportedException>(() => queue.AddJob("job"));
    }

    [Fact]
    public void CurrentJobSettingsKeepsTheDescriptionForTheNextJob()
    {
        Use(Info("a", "A"));
        var queue = new PrintQueue(new PrintServer(), "A");

        var settings = queue.CurrentJobSettings;
        Assert.Same(settings, queue.CurrentJobSettings);
        Assert.Equal(string.Empty, settings.Description);
        Assert.Null(settings.CurrentPrintTicket);

        settings.Description = "Quarterly report";

        Assert.Equal("Quarterly report", queue.CurrentJobSettings.Description);
    }

    [Fact]
    public void JobNameUsesTheCurrentJobSettingsDescriptionAndFallsBackToDocument()
    {
        Use(Info("a", "A"));
        var queue = new PrintQueue(new PrintServer(), "A");
        var jobName = typeof(PrintQueue).Assembly.GetType("System.Printing.QueuePrinter", throwOnError: true)!
            .GetMethod("JobName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.Equal("Document", jobName.Invoke(null, new object[] { queue }));
        queue.CurrentJobSettings.Description = "Quarterly report";
        Assert.Equal("Quarterly report", jobName.Invoke(null, new object[] { queue }));
        queue.CurrentJobSettings.Description = "   ";
        Assert.Equal("Document", jobName.Invoke(null, new object[] { queue }));
    }

    [Fact]
    public void UnsupportedTicketPageSizeFallsBackToTheQueueDefault()
    {
        var capabilities = CoreCapabilities.CreateDefault();
        var defaults = new PrintTicketModel(CommonPageMediaSizes.Letter);
        var model = new PrintTicketModel(new PageMediaSize("NoSuchSize", 1d, 1d));
        var ensure = typeof(PrintQueue).Assembly.GetType("System.Printing.QueuePrinter", throwOnError: true)!
            .GetMethod("EnsureSupportedPageSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        ensure.Invoke(null, new object[] { model, capabilities, defaults });

        Assert.Equal("Letter", model.PageMediaSize.Name);
    }



    [Fact]
    public void CollectionAddsAndEnumerates()
    {
        Use(Info("a", "A"), Info("b", "B"));
        var server = new PrintServer();
        var collection = new PrintQueueCollection(server, new string[0]);

        Assert.Equal(2, collection.Count());
        collection.Add(new PrintQueue(server, "A"));
        Assert.Equal(3, collection.Count());
    }

    [Fact]
    public void UnsupportedQueueCategoriesAreRefusedNotSilentlyEmpty()
    {
        Use(Info("a", "A"));
        var server = new PrintServer();

        Assert.Single(server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local }));
        Assert.Single(server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Queued }));
        Assert.Throws<PlatformNotSupportedException>(() => server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Shared }));
        Assert.Throws<PlatformNotSupportedException>(() => server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Connections }));
    }

    [Fact]
    public void QueueNamesQualifiedWithAnotherServerAreNotResolvedLocally()
    {
        Use(Info("a", "A"));
        var server = new PrintServer();

        Assert.Throws<PlatformNotSupportedException>(() => new PrintQueue(server, "\\\\other-machine\\A"));
        Assert.Equal("A", new PrintQueue(server, "\\\\" + Environment.MachineName + "\\A").Name);
    }

    [Fact]
    public void RefreshRereadsTheQueueAndFailsWhenItIsGone()
    {
        Use(Info("a", "Old name"));
        var queue = new PrintQueue(new PrintServer(), "Old name");

        Use(Info("a", "New name", attributes: ("printer-location", "moved")));
        queue.Refresh();
        Assert.Equal("New name", queue.Name);
        Assert.Equal("moved", queue.Location);

        Use();
        Assert.Throws<InvalidOperationException>(() => queue.Refresh());
    }

    [Fact]
    public void NullServerIsReportedBeforeAnyBackendLookup()
    {
        Use(Info("a", "A"));
        Assert.Throws<ArgumentNullException>(() => new PrintQueue(null!, "A"));
        Assert.Throws<ArgumentNullException>(() => new PrintQueue(null!, "missing"));
    }

    [Theory]
    [InlineData("\\\\MACHINE")]       // server only
    [InlineData("\\\\MACHINE\\")]     // empty queue part
    [InlineData("\\\\\\Q")]          // empty server part
    [InlineData("MACHINE\\Q")]       // not a UNC name
    public void MalformedQueueNamesAreRejectedBeforeAnyLookup(string name)
    {
        Use(Info("a", "A"), Info("MACHINE", "MACHINE"), Info("Q", "Q"));
        var text = name.Replace("MACHINE", Environment.MachineName);
        Assert.Throws<ArgumentException>(() => new PrintQueue(new PrintServer(), text));
    }

    [Fact]
    public void NullServerPathIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new PrintServer((string)null!));
    }

    [Fact]
    public void NameIsASealedOverrideLikeInTheReferenceApi()
    {
        var server = typeof(PrintServer).GetProperty(nameof(PrintServer.Name))!.GetMethod!;
        var queue = typeof(PrintQueue).GetProperty(nameof(PrintQueue.Name))!.GetMethod!;
        Assert.True(server.IsFinal);
        Assert.True(queue.IsFinal);
        Assert.Equal(typeof(PrintServer), server.DeclaringType);
        Assert.Equal(typeof(PrintQueue), queue.DeclaringType);
    }

    [Fact]
    public void QueueCapabilitiesAsXmlComeFromTheBackendsCapabilities()
    {
        Use(Info("a", "A"));
        var queue = new PrintQueue(new PrintServer(), "A");

        using var reader = new StreamReader(queue.GetPrintCapabilitiesAsXml());
        var xml = reader.ReadToEnd();

        // The fake backend answers PrintCapabilities.CreateDefault(): Letter, A4, Legal and Tabloid, both orientations, duplex.
        foreach (var keyword in new[] { "NorthAmericaLetter", "ISOA4", "NorthAmericaLegal", "NorthAmericaTabloid", "Landscape", "TwoSidedLongEdge", "TwoSidedShortEdge" })
        {
            Assert.Contains("psk:" + keyword, xml);
        }
    }
}
