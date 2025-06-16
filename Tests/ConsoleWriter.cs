using System.IO;
using Xunit.Abstractions;

namespace Tests;

public class ConsoleWriter : StringWriter
{
	private readonly ITestOutputHelper _output;

	public ConsoleWriter(ITestOutputHelper output)
	{
		_output = output;
	}

	public override void WriteLine(string? value)
	{
		_output.WriteLine(value);
	}
}