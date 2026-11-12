#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class KeyboardNavigationTests {
    static readonly List<object> Checks=new List<object>();
    static bool passed=true;
    static string context,folder;
    static void Check(bool ok,string name,object actual=null) { passed &= ok;Checks.Add(new{context,name,ok,actual}); }
    static Task Key(ReaderForm form,ushort key,bool shift=false) { return InputFocusTests.Press(form,key,false,shift); }
    static Task<T> JS<T>(ReaderForm form,string code) { return InputFocusTests.JS<T>(form,code); }
    static async Task Wait(ReaderForm form,string code) {
      for(int i=0;i<400;i++) { if(form.Core!=null && await form.Core.ExecuteScriptAsync(code)=="true") return; await Task.Delay(50); }
      throw new Exception("Timed out: "+code);
    }
    static async Task Click(ReaderForm form,string selector) {
      var point=await JS<int[]>(form,"(()=>{const r=document.querySelector("+Program.Json.Serialize(selector)+").getBoundingClientRect();return [r.x+r.width/2,r.y+r.height/2].map(n=>Math.round(n*devicePixelRatio))})()");
      await InputFocusTests.Click(form,form.PointToScreen(new Point(point[0],point[1])));
    }
    static Control Focus(ReaderForm form) { return form.nativeMenu==null?null:QualityTests.Descendants(form.nativeMenu).FirstOrDefault(c=>c.Focused); }
    static string FocusName(ReaderForm form) {var focused=Focus(form);return focused==null?null:focused.Name;}
    static async Task MenuFocus(ReaderForm form,string id) {
      for(int i=0;i<30;i++) { if(FocusName(form)==id)return;await Key(form,9); }
      throw new Exception("Tab did not reach menu item "+id);
    }
    static async Task OpenMenu(ReaderForm form,string id,bool document) {
      if(document) await Click(form,"#"+id);
      else await InputFocusTests.Click(form,QualityTests.Descendants(form).Single(c=>c.Name==id));
      for(int i=0;i<100 && form.nativeMenu==null;i++) await Task.Delay(20);
      if(form.nativeMenu==null) throw new Exception("Menu did not open: "+id);
    }
    static async Task Menu(ReaderForm form,string kind,bool document) {
      await OpenMenu(form,kind,document);
      var expected=form.nativeMenu.Content.Controls.Cast<Control>().Where(c=>c.Visible && c.Enabled && c.TabStop && (c is Button || c is ComboBox || c is NativeWidthSlider)).Select(c=>c.Name).ToArray();
      var forward=new List<string>();
      for(int i=0;i<expected.Length;i++) {await Key(form,9);forward.Add(FocusName(form));}
      Check(forward.SequenceEqual(expected),"Tab visits enabled menu controls in visual order",forward);
      await Key(form,9);Check(FocusName(form)==expected[0],"Tab wraps inside menu",FocusName(form));
      var reverse=new List<string>();
      for(int i=0;i<expected.Length;i++){await Key(form,9,true);reverse.Add(FocusName(form));}
      Check(reverse.SequenceEqual(expected.Reverse()),"Shift+Tab reverses and wraps inside menu",reverse);
      await Key(form,27);
      Check(form.nativeMenu==null,"Escape closes menu");
      Check(document?await JS<bool>(form,"document.activeElement.id==='"+kind+"'"):QualityTests.Descendants(form).Single(c=>c.Name==kind).Focused,"Escape returns focus to trigger");
      await Key(form,13);Check(form.nativeMenu!=null,"Enter on returned trigger reopens menu");
      if(form.nativeMenu==null) await OpenMenu(form,kind,document);
      if(kind=="appearance") {
        await MenuFocus(form,"font-up");
        int before=document?await JS<int>(form,"Number(document.getElementById('font-size').textContent)"):Int32.Parse(form.Preferences.ContainsKey("font-size")?form.Preferences["font-size"]:"16");
        await Key(form,13);await Task.Delay(250);
        int after=document?await JS<int>(form,"Number(document.getElementById('font-size').textContent)"):Int32.Parse(form.Preferences["font-size"]);
        Check(after==before+1 && FocusName(form)=="font-up","Enter runs font action once and retains focus after rebuild",new{before,after,focus=FocusName(form)});
      } else {
        await MenuFocus(form,"language");
        var select=(ComboBox)Focus(form);int before=select.SelectedIndex;
        await Key(form,0x73);Check(select.DroppedDown,"F4 opens select dropdown");await Key(form,27);
        Check(form.nativeMenu!=null && !select.IsDisposed && !select.DroppedDown && select.SelectedIndex==before,"Escape first dismisses select dropdown, retains menu and value",new{menu=form.nativeMenu!=null,disposed=select.IsDisposed,before,after=select.IsDisposed?-1:select.SelectedIndex});
        if(form.nativeMenu!=null) {
          await Key(form,0x73);await Key(form,0x28);await Key(form,27);
          Check(!select.IsDisposed && !select.DroppedDown && select.SelectedIndex==before && L.Preference=="ru","Escape cancels highlighted language without applying it",new{disposed=select.IsDisposed,index=select.IsDisposed?-1:select.SelectedIndex,preference=L.Preference});
          if(!select.IsDisposed) {
            await Key(form,0x73);await Key(form,0x28);await Key(form,13);
            Check(L.Preference=="en" && FocusName(form)=="language","Enter commits selected language and retains menu focus");
            await Key(form,0x73);await Key(form,0x26);await Key(form,13);
            Check(L.Preference=="ru" && FocusName(form)=="language","Enter commits language back in same menu");
            await Key(form,13);Check(form.nativeMenu!=null && FocusName(form)=="language","Enter on closed select does not activate another command");
            var currentSelect=(ComboBox)Focus(form);
            await Key(form,0x73);await Key(form,9);
            Check(!currentSelect.DroppedDown && FocusName(form)=="default-apps","Tab dismisses dropdown and moves to next menu command");
            await Key(form,9,true);await Key(form,0x73);await Key(form,9,true);
            Check(!currentSelect.DroppedDown && FocusName(form)=="theme","Shift+Tab dismisses dropdown and moves to previous menu command");
          }
        }
      }
      if(form.nativeMenu!=null) await Key(form,27);
      QualityTests.Capture(form,Path.Combine(folder,context+"-returned.png"));
    }
    static async Task Help(ReaderForm form,bool document) {
      await OpenMenu(form,"more",document); await MenuFocus(form,"help");await Key(form,13);
      await Wait(form,"document.getElementById('help-dialog').open");
      Check(await JS<bool>(form,"document.activeElement.matches('#help-tabs [aria-selected=true]')"),"Enter on Help opens dialog and focuses selected section");
      await Key(form,9);Check(await JS<bool>(form,"document.activeElement.matches('#help-dialog [role=tabpanel]:not([hidden])')"),"Tab reaches help panel");
      await Key(form,9,true);Check(await JS<bool>(form,"document.activeElement.matches('#help-tabs [aria-selected=true]')"),"Shift+Tab returns to selected help section");
      await Key(form,9,true);Check(await JS<bool>(form,"document.activeElement.id==='help-close'"),"Shift+Tab reaches help close button");
      await Key(form,13);Check(await JS<bool>(form,"!document.getElementById('help-dialog').open"),"Enter closes help");
      await Key(form,0x70);await Wait(form,"document.getElementById('help-dialog').open");await Key(form,0x24);
      foreach(string tab in new[]{"editor","shortcuts","formats","about"}) {
        await Key(form,0x27);string afterArrow=await JS<string>(form,"document.activeElement.id");await Key(form,13);
        Check(await JS<bool>(form,"document.activeElement.id==='help-tab-"+tab+"' && document.activeElement.getAttribute('aria-selected')==='true'"),"Help section remains selected on Enter: "+tab,new{afterArrow,afterEnter=await JS<string>(form,"document.activeElement.id")});
      }
      var sequence=new List<string>();bool contained=true;
      for(int i=0;i<9;i++){await Key(form,9);contained &= await JS<bool>(form,"!!document.activeElement.closest('#help-dialog')");sequence.Add(await JS<string>(form,"document.activeElement.id||document.activeElement.tagName"));}
      Check(contained,"Tab remains within modal help",sequence);
      contained=true;for(int i=0;i<9;i++){await Key(form,9,true);contained &= await JS<bool>(form,"!!document.activeElement.closest('#help-dialog')");}
      Check(contained,"Shift+Tab remains within modal help");
      await Key(form,27);Check(await JS<bool>(form,"!document.getElementById('help-dialog').open"),"Escape closes help from content or links");
      QualityTests.Capture(form,Path.Combine(folder,context+"-closed.png"));
    }
    static async Task Editor(ReaderForm form,string mode) {
      await form.Core.ExecuteScriptAsync("document.getElementById('view-"+mode+"').click()");
      await Wait(form,"!!document.querySelector('.cm-content')");
      await form.Core.ExecuteScriptAsync("var field=document.getElementById('source');field.focus();field.setSelectionRange(0,0)");
      string before=await JS<string>(form,"field.value");
      await Key(form,9);Check(await JS<bool>(form,"field.value==="+Program.Json.Serialize("    "+before)+" && field.contains(document.activeElement)"),"Tab inserts four spaces and keeps editor focus");
      await Key(form,9,true);Check(await JS<bool>(form,"field.value==="+Program.Json.Serialize(before)+" && field.contains(document.activeElement)"),"Shift+Tab removes indent");
      await Key(form,9,true);Check(await JS<bool>(form,"field.value==="+Program.Json.Serialize(before)+" && field.contains(document.activeElement)"),"Shift+Tab without indent keeps text and focus");
      await form.Core.ExecuteScriptAsync("field.setSelectionRange(field.value.length,field.value.length)");
      await Key(form,13);Check(await JS<string>(form,"field.value")==before+"\n","Enter inserts one newline");
      before=await JS<string>(form,"field.value");await Key(form,27);
      Check(await JS<bool>(form,"field.value==="+Program.Json.Serialize(before)+" && field.contains(document.activeElement)"),"Escape alone preserves text and caret focus");
      await Key(form,9);Check(await JS<bool>(form,"!field.contains(document.activeElement) && field.value==="+Program.Json.Serialize(before)),"Escape then Tab exits editor without insertion");
      await form.Core.ExecuteScriptAsync("field.focus()");await Key(form,27);await Key(form,9,true);
      Check(await JS<bool>(form,"!field.contains(document.activeElement) && field.value==="+Program.Json.Serialize(before)),"Escape then Shift+Tab exits editor backwards");
      await form.Core.ExecuteScriptAsync("field.focus();field.setSelectionRange(2,2)");
      if(mode=="split") await form.Core.ExecuteScriptAsync("if(document.getElementById('outline-toggle').getAttribute('aria-expanded')!=='true')document.getElementById('outline-toggle').click();field.focus()");
      bool outline=await JS<bool>(form,"document.getElementById('outline-toggle').getAttribute('aria-expanded')==='true'");
      await Key(form,0x70);await Wait(form,"document.getElementById('help-dialog').open");await Key(form,27);
      Check(await JS<bool>(form,"field.contains(document.activeElement) && field.selectionStart===2"),"Escape from F1 help restores editor and caret");
      Check(await JS<bool>(form,"document.getElementById('outline-toggle').getAttribute('aria-expanded')==='true'")==outline,"Escape from help leaves background outline unchanged");
      await form.Core.ExecuteScriptAsync("document.getElementById('search-toggle').click()");
      await Key(form,0x70);await Wait(form,"document.getElementById('help-dialog').open");await Key(form,27);
      Check(await JS<bool>(form,"!document.getElementById('help-dialog').open && !document.getElementById('searchbar').hidden && document.activeElement.id==='search-input'"),"Escape closes help first and restores the open search");
      await Key(form,27);Check(await JS<bool>(form,"document.getElementById('searchbar').hidden"),"Next Escape closes search");
      QualityTests.Capture(form,Path.Combine(folder,context+".png"));
    }
    static async Task Formatting(ReaderForm form) {
      await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click()");
      await Wait(form,"!!document.querySelector('.cm-content') && !document.body.inert");
      for(int index=0;index<3;index++) {
        await form.Core.ExecuteScriptAsync("var field=document.getElementById('source');field.value='Alpha';field.dispatchEvent(new Event('input',{bubbles:true}));field.setSelectionRange(0,5);var menu=document.querySelectorAll('.format-menu')["+index+"];menu.querySelector('summary').focus()");
        await Key(form,13);Check(await JS<bool>(form,"menu.open"),"Enter opens formatting menu "+index);
        await Key(form,9);Check(await JS<bool>(form,"document.activeElement===menu.querySelector('button')"),"Tab reaches first formatting command "+index);
        await Key(form,9,true);Check(await JS<bool>(form,"document.activeElement===menu.querySelector('summary')"),"Shift+Tab returns to formatting trigger "+index);
        await Key(form,9);if(index==0)await Key(form,9);
        await Key(form,13);
        string expected=index==0?"# Alpha":index==1?"- Alpha":"> Alpha";
        Check(await JS<bool>(form,"field.value==="+Program.Json.Serialize(expected)+" && !menu.open && field.contains(document.activeElement)"),"Enter applies selected formatting once and returns to editor "+index,await JS<string>(form,"field.value"));
        await form.Core.ExecuteScriptAsync("menu.querySelector('summary').focus()");await Key(form,13);await Key(form,9);await Key(form,27);
        Check(await JS<bool>(form,"!menu.open && field.contains(document.activeElement) && field.value==="+Program.Json.Serialize(expected)),"Escape cancels formatting menu without changing text "+index);
      }
    }
    static async Task TabList(ReaderForm form) {
      var files=Enumerable.Range(1,12).Select(i=>Path.Combine(Program.DataRoot,"Navigation tab "+i+".md")).ToArray();
      foreach(string path in files)File.WriteAllText(path,"# "+Path.GetFileNameWithoutExtension(path));
      form.ReceiveArguments(files);
      await Wait(form,"document.querySelectorAll('.tab-item').length===13 && !document.body.inert && document.getElementById('tab-strip').classList.contains('is-overflow')");
      await Click(form,"#tabs-menu-toggle");
      Check(await JS<bool>(form,"document.activeElement.id==='tabs-filter'"),"Click opens tab list with search focused");
      await Key(form,9);Check(await JS<bool>(form,"document.activeElement===document.querySelector('#tabs-menu-list button')"),"Tab reaches first document in tab list");
      await Key(form,9,true);Check(await JS<bool>(form,"document.activeElement.id==='tabs-filter'"),"Shift+Tab returns to tab search");
      await Key(form,27);Check(await JS<bool>(form,"document.getElementById('tabs-menu').hidden && document.activeElement.id==='tabs-menu-toggle'"),"Escape closes tab list and returns focus");
      await Key(form,13);Check(await JS<bool>(form,"!document.getElementById('tabs-menu').hidden && document.activeElement.id==='tabs-filter'"),"Enter reopens tab list");
      await Key(form,13);await Wait(form,"document.getElementById('tabs-menu').hidden && !document.body.inert");
      Check(await JS<bool>(form,"document.getElementById('filename').textContent==='Navigation.md'"),"Enter in tab search activates first result");
    }
    internal static async Task Run(ReaderForm form) {
      folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"keyboard-navigation-checks");Directory.CreateDirectory(folder);
      try {
        form.TopMost=true;form.Show();form.Activate();await Task.Delay(200);
        await form.Call("language",new object[]{"ru"});
        string file=Path.Combine(Program.DataRoot,"Navigation.md");File.WriteAllText(file,"# Keyboard\n\nAlpha\nBeta");
        foreach(string theme in new[]{"light","dark"}) {
          form.ReleaseBrowser();form.Store.Entries.Clear();await form.Call("theme",new object[]{theme});
          foreach(bool document in new[]{false,true}) {
            if(document) {form.ReceiveArguments(new[]{file});await Wait(form,"!!document.querySelector('#document h1') && !document.body.inert");}
            foreach(string menu in new[]{"more","appearance"}) {
              context=theme+"-"+(document?"document":"empty")+"-"+menu;
              try{await Menu(form,menu,document);}catch(Exception e){Check(false,"Scenario failed",e.ToString());form.CloseNativeMenu(false);}
            }
            context=theme+"-"+(document?"document":"empty")+"-help";
            try{await Help(form,document);}catch(Exception e){Check(false,"Scenario failed",e.ToString());}
            if(!document)form.ReleaseBrowser();
          }
          foreach(string mode in new[]{"split","source"}) {
            context=theme+"-editor-"+mode;
            try{await Editor(form,mode);}catch(Exception e){Check(false,"Scenario failed",e.ToString());}
          }
          context=theme+"-formatting";
          try{await Formatting(form);}catch(Exception e){Check(false,"Scenario failed",e.ToString());}
          context=theme+"-tab-list";
          try{await TabList(form);}catch(Exception e){Check(false,"Scenario failed",e.ToString());}
        }
        File.WriteAllText(Path.Combine(folder,"result.json"),Program.Json.Serialize(new{ok=passed,checks=Checks,dpi=form.DeviceDpi,profile=Program.DataRoot,method="Windows SendInput; separate test profile"}));
      } catch(Exception e){File.WriteAllText(Path.Combine(folder,"result.json"),Program.Json.Serialize(new{ok=false,error=e.ToString(),checks=Checks}));}
      await form.Call("finish-close",new object[0]);
    }
  }
}
#endif
