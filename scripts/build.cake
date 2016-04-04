using System;
using System.Diagnostics;
using IOPath = System.IO.Path;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    // Clean directories.
    CleanDirectory("../output");
    CleanDirectory("../output/bin");
    CleanDirectories("../src/**/bin/" + configuration);
});

Task("Restore-NuGet-Packages")
  //  .IsDependentOn("Clean")
    .Does(() =>
{
    NuGetRestore("../DynamicSoapWebService.sln");
});

Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
	MSBuild("../DynamicSoapWebService.sln", settings =>
		settings.SetConfiguration(configuration));
});

Task("CopyFiles")
    .IsDependentOn("Build")
    .Does(() =>
{
    var globStart = "../src/**/bin/" + configuration + "/**/";
    var files = GetFiles(globStart + "*.dll") 
        + GetFiles(globStart + "*.exe")
		+ GetFiles(globStart + "*.pdb");

    // Copy all exe and dll files to the output directory.
	if (!System.IO.Directory.Exists("../output/bin"))
	{
		System.IO.Directory.CreateDirectory("../output/bin");
	}
    CopyFiles(files, "../output/bin");
}); 

Task("Run")
	.IsDependentOn("CopyFiles")
    .Does(() =>
{
    ProcessStartInfo startInfo;
    startInfo = new ProcessStartInfo() 
	{
		WorkingDirectory = IOPath.GetFullPath("../output/bin"),
	    FileName = IOPath.GetFullPath("../output/bin/SoapWebService.exe"),
		Verb = "runas"
	};
	
	// start the service process elevated so we can force port binding.
	Process.Start(startInfo);
	
	// start the client. no elevation needed.
	string path = IOPath.GetFullPath("../output/bin/SoapWebServiceClient.exe");
	Process.Start(path);
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Build");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);