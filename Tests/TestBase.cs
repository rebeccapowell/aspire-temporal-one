using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Tests;

public abstract class TestBase : IDisposable
{
	private readonly TextWriter _consoleWriter;

	protected TestBase(ITestOutputHelper output)
	{
		var factory = new LoggerFactory();
		factory.AddXUnit(output);
		LoggerFactory = factory;
		_consoleWriter = new ConsoleWriter(output);
		Console.SetOut(_consoleWriter);
	}

	protected ILoggerFactory LoggerFactory { get; }

	public void Dispose()
	{
		_consoleWriter?.Dispose();
	}
}