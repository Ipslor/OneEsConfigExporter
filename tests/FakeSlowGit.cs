using System;
using System.IO;
using System.Linq;
using System.Threading;
class FakeSlowGit {
 static int Main(string[] args){string folder="";int i=Array.IndexOf(args,"-C");if(i>=0)folder=args[i+1];
  if(args.Contains("--version")){Console.WriteLine("git version 2.47.0");return 0;}
  if(args.Contains("--is-inside-work-tree")){Console.WriteLine("true");return 0;}
  if(args.Contains("--absolute-git-dir")){Console.WriteLine(Path.Combine(folder,".git"));return 0;}
  if(args.Contains("status")){if(Environment.GetEnvironmentVariable("EXPORTER_TEST_SLOW_GIT")=="1")Thread.Sleep(30000);Console.Error.WriteLine("warning: unknown useful warning");return 0;}return 0;
 }
}
