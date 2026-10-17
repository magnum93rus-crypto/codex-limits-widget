using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
class ImportUia : ITypeLibImporterNotifySink {
    [DllImport("oleaut32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]
    static extern void LoadTypeLibEx(string path,int kind,[MarshalAs(UnmanagedType.Interface)]out object typeLib);
    public void ReportEvent(ImporterEventKind kind,int code,string message) { }
    public Assembly ResolveRef(object typeLib) {throw new InvalidOperationException("Unexpected type library dependency");}
    static void Main(string[] args) {
        object library;LoadTypeLibEx(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"UIAutomationCore.dll"),2,out library);
        try {
            string target=Path.GetFullPath(args[0]);
            var assembly=new TypeLibConverter().ConvertTypeLibToAssembly(library,target,TypeLibImporterFlags.None,new ImportUia(),null,null,"NativeUIA",null);
            assembly.Save(Path.GetFileName(target));
        }finally {Marshal.ReleaseComObject(library);}
    }
}
