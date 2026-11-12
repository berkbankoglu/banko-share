if (args.Length == 2 && args[0] == "--release-smoke")
{
    await ReleaseSmoke.Run(args[1]);
    return;
}

var passed = 0;
passed += await ProtocolTests.Run();
passed += LayoutTests.Run();
passed += SwitchEngineTests.Run();
passed += await NetworkTests.Run();
passed += await UpdateTests.Run();
passed += InputStateTests.Run();
Console.WriteLine($"{passed} tests passed. No OS input was injected.");
