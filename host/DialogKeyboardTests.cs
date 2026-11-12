#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace MdReader {
  static class DialogKeyboardTests {
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern IntPtr GetFocus();
    [DllImport("user32")] static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int count);
    static readonly List<object> checks=new List<object>();
    static bool passed=true;
    static string context,folder;
    sealed class FocusState { public long handle;public int id;public string text,kind,identity; }
    static async Task<FocusState> Focus() {
      var handle=GetFocus();var text=new StringBuilder(512);var kind=new StringBuilder(128);
      GetWindowText(handle,text,text.Capacity);GetClassName(handle,kind,kind.Capacity);
      var state=new FocusState {handle=handle.ToInt64(),id=GetDlgCtrlID(handle),text=text.ToString(),kind=kind.ToString()};
      return await Task.Run(()=>{
        var element=AutomationElement.FocusedElement;
        if(element==null)throw new Exception("Focused dialog element unavailable");
        state.text=element.Current.Name.Replace("&","");
        var runtime=element.GetRuntimeId();state.identity=runtime==null?state.handle+":"+element.Current.AutomationId+":"+state.text:String.Join("/",runtime);
        return state;
      });
    }
    static void Check(bool ok,string name,object actual=null) {passed &= ok;checks.Add(new{context,name,ok,actual});}
    static Task Key(ReaderForm form,ushort key,bool control=false,bool shift=false) {return InputFocusTests.Press(form,key,control,shift);}
    static Task<T> JS<T>(ReaderForm form,string code) {return InputFocusTests.JS<T>(form,code);}
    static async Task Wait(Func<Task<bool>> condition,string name) {
      for(int i=0;i<240;i++){if(await condition())return;await Task.Delay(50);}
      throw new TimeoutException(name);
    }
    static Task Ready(ReaderForm form) {return Wait(async()=>form.UiReady && await JS<bool>(form,"!document.body.inert && !!document.getElementById('source')"),"Document ready");}
    static Task Dialog(ReaderForm form) {return Wait(()=>Task.FromResult(GetForegroundWindow()!=form.Handle && InputFocusTests.OwnsForeground(form)),"Owned dialog opens");}
    static Task Returned(ReaderForm form) {return Wait(()=>Task.FromResult(GetForegroundWindow()==form.Handle),"Dialog returns to owner");}
    static async Task FileName(ReaderForm form) {
      var seen=new List<FocusState>();
      for(int i=0;i<30;i++){var f=await Focus();seen.Add(f);if((f.id==1148 || f.id==1001) && f.kind=="Edit")return;await Key(form,9);}
      throw new Exception("File name field not reachable by Tab: "+Program.Json.Serialize(seen));
    }
    static async Task Target(ReaderForm form,Func<FocusState,bool> match) {
      for(int i=0;i<30;i++){if(match(await Focus()))return;await Key(form,9);}
      throw new Exception("Dialog command not reachable by Tab");
    }
    static async Task PathText(ReaderForm form,string path) {
      await FileName(form);await Key(form,0x41,true);await InputFocusTests.TypeText(form,path);
    }
    static async Task TraverseFileDialog(ReaderForm form) {
      await FileName(form);var first=await Focus();await Key(form,9);
      Check((await Focus()).identity!=first.identity,"Tab leaves filename",await Focus());
      await Key(form,9,false,true);Check((await Focus()).identity==first.identity,"Shift+Tab returns to filename",await Focus());
      var visited=new List<FocusState>();bool contained=true;
      for(int i=0;i<30;i++){
        await Key(form,9);var current=await Focus();visited.Add(current);
        contained &= GetForegroundWindow()!=form.Handle && InputFocusTests.OwnsForeground(form);
        if(current.identity==first.identity)break;
      }
      Check(contained && visited.Last().identity==first.identity,"Tab wraps within file dialog",visited);
      Check(visited.Any(f=>f.id==1) && visited.Any(f=>f.id==2),"Open/Save and Cancel reachable by Tab");
      QualityTests.Capture(GetForegroundWindow(),Path.Combine(folder,context+".png"));
    }
    static async Task Open(ReaderForm form,string file,bool empty) {
      await Key(form,0x4F,true);await Dialog(form);await TraverseFileDialog(form);await Key(form,27);await Returned(form);
      Check(empty?form.Core==null:await JS<bool>(form,"document.getElementById('source').contains(document.activeElement)"),"Escape returns focus without loading or losing document");
      await Key(form,0x4F,true);await Dialog(form);await Target(form,f=>f.id==2);await Key(form,13);await Returned(form);
      Check(empty?form.Core==null:await JS<bool>(form,"document.querySelectorAll('.tab-item').length===1"),"Enter on Cancel preserves current state");
      await Key(form,0x4F,true);await Dialog(form);var chooser=GetForegroundWindow();
      await PathText(form,Path.Combine(Program.DataRoot,"missing.md"));await Key(form,13);
      await Wait(()=>Task.FromResult(GetForegroundWindow()!=chooser),"Missing-file warning");await Key(form,27);
      Check(GetForegroundWindow()==chooser,"Escape dismisses missing-file warning before chooser");
      await PathText(form,file);await Key(form,13);await Ready(form);
      Check(await JS<string>(form,"document.getElementById('source').value")==File.ReadAllText(file),"Enter opens exact selected file");
    }
    static async Task Edit(ReaderForm form,string text) {
      await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click();var field=document.getElementById('source');field.value="+Program.Json.Serialize(text)+";field.dispatchEvent(new Event('input',{bubbles:true}));field.focus();field.setSelectionRange(2,2)");
    }
    static async Task Preserved(ReaderForm form,string text,string label) {
      await Returned(form);await Ready(form);
      Check(await JS<string>(form,"document.getElementById('source').value")==text,label+": exact draft retained");
      Check(await JS<bool>(form,"document.getElementById('source').contains(document.activeElement) && document.getElementById('source').selectionStart===2"),label+": editor focus and caret restored",await JS<string>(form,"document.activeElement.tagName+':'+document.activeElement.id"));
      await Key(form,13);bool inserted=GetForegroundWindow()==form.Handle && await JS<string>(form,"document.getElementById('source').value")==text.Insert(2,"\n");
      Check(inserted,label+": next Enter inserts one newline at restored caret");
      if(inserted)await Key(form,0x5A,true);
      else if(GetForegroundWindow()!=form.Handle && InputFocusTests.OwnsForeground(form))await Key(form,27);
    }
    static async Task Save(ReaderForm form,string text,string path) {
      await Key(form,0x53,true);await Dialog(form);await TraverseFileDialog(form);await Key(form,27);await Preserved(form,text,"Save Escape");
      await Edit(form,text);await Key(form,0x53,true);await Dialog(form);await Target(form,f=>f.id==2);await Key(form,13);await Preserved(form,text,"Save Cancel Enter");
      await Edit(form,text);await Key(form,0x53,true);await Dialog(form);await PathText(form,path);await Key(form,13);
      await Wait(()=>Task.FromResult(File.Exists(path)),"Saved file exists");await Ready(form);
      Check(File.ReadAllText(path)==text,"Enter saves exact draft with chosen filename");
      await Edit(form,text+" changed");await Key(form,0x53,true,true);await Dialog(form);await PathText(form,path);var chooser=GetForegroundWindow();await Key(form,13);
      await Wait(()=>Task.FromResult(GetForegroundWindow()!=chooser),"Overwrite confirmation");var warning=GetForegroundWindow();await Key(form,27);
      Check(GetForegroundWindow()==warning && File.ReadAllText(path)==text,"Escape in native Yes/No confirmation does not overwrite or dismiss it");
      QualityTests.Capture(GetForegroundWindow(),Path.Combine(folder,context+"-overwrite.png"));
      await Target(form,f=>f.text=="Нет" || f.text=="No");var no=await Focus();await Key(form,9);var yes=await Focus();Check(yes.text=="Да" || yes.text=="Yes","Tab moves from No to Yes in overwrite confirmation",yes);
      await Key(form,9,false,true);Check((await Focus()).identity==no.identity,"Shift+Tab returns to No in overwrite confirmation",await Focus());
      await Key(form,13);Check(GetForegroundWindow()==chooser && File.ReadAllText(path)==text,"Enter on No returns to Save As without changing disk");
      await Key(form,27);await Preserved(form,text+" changed","Save As Escape");
    }
    static async Task Close(ReaderForm form,string text,string output) {
      await Edit(form,text);await Key(form,0x57,true);await Dialog(form);
      Check((await Focus()).text==L.T("Сохранить"),"Save initially focused",await Focus());
      await Key(form,9);Check((await Focus()).text==L.T("Не сохранять"),"Tab reaches Discard",await Focus());
      await Key(form,9);Check((await Focus()).text==L.T("Отмена"),"Tab reaches Cancel",await Focus());
      await Key(form,9);Check((await Focus()).text==L.T("Сохранить"),"Tab wraps to Save",await Focus());
      await Key(form,9,false,true);Check((await Focus()).text==L.T("Отмена"),"Shift+Tab wraps to Cancel",await Focus());
      QualityTests.Capture(GetForegroundWindow(),Path.Combine(folder,context+".png"));
      await Key(form,13);await Preserved(form,text,"Close Cancel Enter");
      await Edit(form,text);await Key(form,0x57,true);await Dialog(form);await Key(form,27);await Preserved(form,text,"Close Escape");
      await Edit(form,text);form.BeginInvoke(new Action(form.Close));await Dialog(form);await Key(form,27);await Preserved(form,text,"Window Close Escape");
      var closeButton=QualityTests.Descendants(form).OfType<CaptionButton>().Single(b=>b.AccessibleName==L.T("Закрыть"));
      await Edit(form,text);await InputFocusTests.Click(form,closeButton);await Dialog(form);await Key(form,27);await Preserved(form,text,"Caption click then Escape");
      await Edit(form,text);await Key(form,0x57,true);await Dialog(form);await Key(form,13);await Dialog(form);
      await FileName(form);await Key(form,27);await Preserved(form,text,"Close Save then chooser Escape");
      await Edit(form,text);await Key(form,0x57,true);await Dialog(form);await Key(form,13);await Dialog(form);await PathText(form,output);await Key(form,13);
      await Wait(()=>Task.FromResult(File.Exists(output) && form.Core==null),"Save closes last tab");
      Check(File.ReadAllText(output)==text,"Enter Save writes draft and closes tab");
      await Key(form,0x4E,true);await Ready(form);await Edit(form,"Discard only this QA draft");await Key(form,0x57,true);await Dialog(form);await Key(form,9);await Key(form,13);
      await Wait(()=>Task.FromResult(form.Core==null),"Discard closes last tab");Check(true,"Enter Discard closes draft without a file chooser");
    }
    internal static async Task Run(ReaderForm form) {
      folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dialog-keyboard-checks");Directory.CreateDirectory(folder);
      try {
        form.TopMost=true;form.Show();form.Activate();await Task.Delay(250);await form.Call("language",new object[]{"ru"});
        foreach(string theme in new[]{"light","dark"}) {
          form.ReleaseBrowser();form.Store.Entries.Clear();await form.Call("theme",new object[]{theme});
          string file=Path.Combine(Program.DataRoot,theme+"-open.md");File.WriteAllText(file,"# Dialog keyboard\n\n"+theme);
          context=theme+"-empty-open";await Open(form,file,true);
          await Edit(form,File.ReadAllText(file));context=theme+"-document-open";await Open(form,file,false);
          await Key(form,0x57,true);await Wait(()=>Task.FromResult(form.Core==null),"Close unchanged fixture");
          await Key(form,0x4E,true);await Ready(form);string text="Draft "+theme+"\nРусский text";await Edit(form,text);
          context=theme+"-save";await Save(form,text,Path.Combine(Program.DataRoot,theme+"-saved.md"));
          await Key(form,0x53,true);await Ready(form);await Key(form,0x57,true);await Wait(()=>Task.FromResult(form.Core==null),"Close saved fixture");
          await Key(form,0x4E,true);await Ready(form);context=theme+"-close";await Close(form,text,Path.Combine(Program.DataRoot,theme+"-close-save.md"));
        }
      } catch(Exception error) {Check(false,"Scenario stopped",error.ToString());}
      File.WriteAllText(Path.Combine(folder,"result.json"),Program.Json.Serialize(new{ok=passed,checks,profile=Program.DataRoot,method="Real Windows dialogs, shared SendInput helpers, isolated fixtures",dpi=form.DeviceDpi}));
      form.ReleaseBrowser();form.Close();
    }
  }
}
#endif
