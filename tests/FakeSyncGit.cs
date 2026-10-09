using System;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
class FakeSyncGit {
 static string Quote(string value){var text=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"')text.Append('\\',slashes*2+1);else text.Append('\\',slashes);text.Append(c);slashes=0;}return text.Append('\\',slashes*2).Append('"').ToString();}
 static int Main(string[] args){
  Console.OutputEncoding=new UTF8Encoding(false);string git=Environment.GetEnvironmentVariable("ONEC_SYNC_TEST_GIT"),remote=Environment.GetEnvironmentVariable("ONEC_SYNC_TEST_REMOTE");if(String.IsNullOrEmpty(git)||String.IsNullOrEmpty(remote))return 2;
  int queryDelay;if(args.Contains("config")&&args.Contains("--get-regexp")&&Int32.TryParse(Environment.GetEnvironmentVariable("ONEC_SYNC_TEST_QUERY_DELAY_MS"),out queryDelay)&&queryDelay>0&&queryDelay<4000)Thread.Sleep(queryDelay);
  var command=new StringBuilder();foreach(string arg in args){if(command.Length>0)command.Append(' ');command.Append(Quote(arg=="https://github.com/test/sync.git"?remote:arg));}
  using(var process=Process.Start(new ProcessStartInfo{FileName=git,Arguments=command.ToString(),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8})){
   Task<string> stdout=process.StandardOutput.ReadToEndAsync(),stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(30000)){process.Kill();return 124;}Task.WaitAll(stdout,stderr);Console.Write(stdout.Result);Console.Error.Write(stderr.Result);return process.ExitCode;
  }
 }
}
