using System;
using System.IO;
using System.Text;
using System.Threading;
class FakeDesigner {
 static string Value(string[] args,string flag){int index=Array.IndexOf(args,flag);return index>=0&&index+1<args.Length?args[index+1]:"";}
 static int Main(string[] args){
  string folder=Value(args,"/DumpConfigToFiles"),source=Value(args,"/S"),log=Value(args,"/Out");Directory.CreateDirectory(folder);Thread.Sleep(100);
  if(source.Contains("fail")){File.WriteAllText(log,"Тестовый отказ: нет доступа к базе",Encoding.UTF8);return 1;}
  File.WriteAllText(Path.Combine(folder,"Configuration.xml"),"<MetaDataObject><Configuration><Properties><Name>ТестоваяКонфигурация</Name></Properties></Configuration></MetaDataObject>",Encoding.UTF8);
  File.WriteAllText(Path.Combine(folder,"ConfigDumpInfo.xml"),"<ConfigDumpInfo />");File.WriteAllText(Path.Combine(folder,"Module.bsl"),"Сообщить(\"тест\");",Encoding.UTF8);File.WriteAllText(log,"Тестовая выгрузка завершена",Encoding.UTF8);return 0;
 }
}
