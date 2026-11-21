using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BootScanner.Core;
using BootScanner.Core.Export;
using BootScanner.Core.Models;
using BootScanner.Core.Parsing;

namespace BootScanner.Tests;

// Inputs are generated inside unit tests. No Boot project assets or fixture files are shipped.
public sealed class CoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "scanner-test-" + Guid.NewGuid());
    public CoreTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Theory]
    [InlineData("=1+1")]
    [InlineData(" +1+1")]
    [InlineData("@SUM(A1)")]
    [InlineData("-1+2")]
    public void CsvFormulaLikeTextIsProtected(string value) => Assert.StartsWith("'", CsvExporter.Escape(value));

    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("abc", "abc")]
    public void CsvEscapingPreservesText(string value, string expected) => Assert.Equal(expected, CsvExporter.Escape(value));

    [Fact]
    public void XmlRejectsExternalEntities() => Assert.Throws<System.Xml.XmlException>(() =>
        XmlUtil.ParseText("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///should-not-be-read'>]><a>&x;</a>"));

    [Fact]
    public void XmlRejectsExcessiveNesting() => Assert.Throws<System.Xml.XmlException>(() =>
        XmlUtil.ParseText(string.Concat(Enumerable.Repeat("<a>", 300)) + string.Concat(Enumerable.Repeat("</a>", 300))));

    [Fact]
    public void RuntimeRootDoesNotProveTc3()
    {
        File.WriteAllText(Path.Combine(_directory, "CurrentConfig.xml"), "<TcBootProject/>");
        Assert.Equal(TwinCatGeneration.Unknown, TwinCatBootDetector.Detect(_directory).Generation);
    }

    [Fact]
    public void ExplicitRuntimeHardwareHasSourceAndNoGuessedLinks()
    {
        var result = new CurrentConfigParser().ParseDocument(XDocument.Parse(
            "<TcBootProject><ProjectInfo><Devices><Device><Name>unit-device</Name><Type>94</Type><Boxes><Box><Name>unit-box</Name><FieldbusAddress>7</FieldbusAddress></Box></Boxes></Device></Devices></ProjectInfo></TcBootProject>"));
        var device = Assert.Single(result.Roots);
        Assert.Equal(TopologyKind.Device, device.Kind);
        Assert.Null(device.OrderNumber);
        var box = Assert.Single(device.Children);
        Assert.Contains("7", box.Address);
        Assert.Contains("ProjectInfo", box.SourceFile);
        Assert.Empty(box.LinkedSymbols);
        Assert.DoesNotContain("主站", result.Summary);
    }

    [Fact]
    public void TypeDefinitionsAreNotHardwareInstances()
    {
        var result = new CurrentConfigParser().ParseDocument(XDocument.Parse(
            "<TcBootProject><ProjectInfo><DataTypes><Device><Name>not-hardware</Name></Device></DataTypes></ProjectInfo></TcBootProject>"));
        Assert.Empty(result.Roots);
    }

    [Fact]
    public void ExplicitProjectReferenceIsDisplayedWithoutAccessingPath()
    {
        var result = new CurrentConfigParser().ParseDocument(XDocument.Parse(
            "<TcSmProject><Project><Plc><Project Name='unit-project' PrjFilePath='unavailable.plcproj'/></Plc></Project></TcSmProject>"));
        Assert.Contains(result.Roots.SelectMany(n => n.EnumerateDepthFirst()), n => n.Name.Contains("unavailable.plcproj"));
    }

    [Fact]
    public void OptionalTpyAbsenceIsNotCorruption()
    {
        File.WriteAllBytes(Path.Combine(_directory, "CurrentConfig.tsm"), TsmHardwareFile.OleCompoundSignature);
        var scan = new BootScanService().Scan(_directory);
        Assert.Contains(scan.Status, s => s.FileName == "*.tpy" && s.Outcome == ParseOutcome.Unavailable);
    }

    [Fact]
    public void CancelledScanDoesNotReadFiles() => Assert.ThrowsAny<OperationCanceledException>(() =>
        new BootScanService().Scan(_directory, new CancellationToken(true)));

    [Fact]
    public void CsvCannotOverwriteSourceDirectory()
    {
        var path = Path.Combine(_directory, "CurrentConfig.xml");
        File.WriteAllText(path, "<TcConfig/>");
        Assert.Throws<ArgumentException>(() => CsvExporter.Export(new BootScanService().Scan(_directory), path));
        Assert.Equal("<TcConfig/>", File.ReadAllText(path));
    }

    [Fact]
    public void CsvWritesBomAndSourceColumn()
    {
        var path = Path.Combine(_directory, "table.csv");
        var result = new BootScanResult { BootDirectory = "", Topology = new[] { new TopologyNode { Name = "unit-node", SourceFile = "field-source" } } };
        var files = CsvExporter.Export(result, path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(files.VariablesPath).Take(3));
        Assert.Contains("field-source", File.ReadAllText(files.TopologyPath));
    }

    [Fact]
    public void NativeStorageReadsOnlyDeclaredMetadataAndKeepsFileUnchanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_directory, "CurrentConfig.tsm");
        File.WriteAllBytes(path, CompoundTestData.Create());
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var result = new BootScanService().Scan(_directory);
        Assert.Equal(TwinCatGeneration.TwinCAT2, result.Kind!.Generation);
        Assert.Contains(result.Topology.SelectMany(n => n.EnumerateDepthFirst()), n => n.Name.Contains("unit-target"));
        Assert.Contains(result.Topology.SelectMany(n => n.EnumerateDepthFirst()), n => n.Name.Contains("unverified.tpy"));
        Assert.Empty(result.Symbols);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public void NativeStorageHandlesBadXmlLengthAsDiagnostic()
    {
        if (!OperatingSystem.IsWindows()) return;
        var bytes = CompoundTestData.Create();
        BitConverter.GetBytes(123).CopyTo(bytes, 3 * 512);
        File.WriteAllBytes(Path.Combine(_directory, "CurrentConfig.tsm"), bytes);
        var result = new BootScanService().Scan(_directory);
        Assert.Contains(result.Status, s => s.FileName == "CurrentConfig.tsm" && s.Outcome == ParseOutcome.Failed);
    }

    [Fact]
    public void DisclaimerIncludesIdentityLimitsAndNonExcludableLiability()
    {
        Assert.Contains("非 Beckhoff 官方产品", LegalNotice.Footer);
        Assert.Contains("不受本说明影响", LegalNotice.FullText);
        Assert.Contains("WSM", LegalNotice.FullText);
        Assert.Contains("脱敏", LegalNotice.FullText);
    }
}
