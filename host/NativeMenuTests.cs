#if TESTING
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class NativeMenuTests {
    static Control Control(ReaderForm form,string name) { return QualityTests.Descendants(form).Single(c=>c.Name==name); }
    static Button Button(ReaderForm form,string name) { return (Button)Control(form,name); }
    static void Assert(bool condition,string message) { if(!condition) throw new Exception(message); }
    static async Task Poll(Func<Task<bool>> condition,string label) {
      for(int i=0;i<600;i++) { if(await condition()) return; await Task.Delay(50); }
      throw new Exception("Timed out: "+label);
    }
    static int Differences(Bitmap before,Bitmap after,Rectangle region) {
      int changed=0;
      for(int y=region.Top;y<region.Bottom;y++) for(int x=region.Left;x<region.Right;x++) if(before.GetPixel(x,y)!=after.GetPixel(x,y)) changed++;
      return changed;
    }
    static Bitmap MenuCapture(ReaderForm form,string path,string controlId=null) {
      QualityTests.Capture(form,path);
      var control=controlId==null?(Control)form.nativeMenu:Control(form,controlId);
      var bounds=form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
      using(var image=new Bitmap(path)) return image.Clone(bounds,image.PixelFormat);
    }
    static async Task OpenDocumentMenu(ReaderForm form,string kind) {
      await form.Core.ExecuteScriptAsync("document.getElementById('"+kind+"').click()");
      await Poll(()=>Task.FromResult(form.nativeMenu!=null && form.nativeMenu.Name==kind+"-menu"),"document "+kind);
      form.nativeMenu.Trigger.Focus(); form.Refresh(); await Task.Delay(80);
      Assert(await form.Core.ExecuteScriptAsync("document.getElementById('"+kind+"-menu').hidden")=="true","Duplicate HTML menu visible");
    }
    internal static async Task Run(ReaderForm form) {
      var folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native-menu-checks"); Directory.CreateDirectory(folder);
      var checks=new List<object>(); var timings=new List<double>();
      try {
        form.TopMost=true; form.Show(); form.Activate(); await Task.Delay(150);
        foreach(string language in new[]{"ru","en"}) foreach(string theme in new[]{"light","dark"}) foreach(string kind in new[]{"more","appearance"}) {
          await form.Call("language",new object[]{language}); await form.Call("theme",new object[]{theme});
          form.Size=new Size(1120,820); form.Refresh(); await Task.Delay(80);
          string name=language+"-"+theme+"-"+kind;
          string beforePath=Path.Combine(folder,name+"-before.png"), afterPath=Path.Combine(folder,name+"-open.png");
          QualityTests.Capture(form,beforePath);
          var clock=Stopwatch.StartNew(); Button(form,kind).PerformClick(); form.Update(); clock.Stop(); timings.Add(clock.Elapsed.TotalMilliseconds);
          Assert(form.nativeMenu!=null && form.View==null,"Opening "+kind+" started WebView");
          await Task.Delay(80); QualityTests.Capture(form,afterPath);
          var differences=new Dictionary<string,int>();
          using(var before=new Bitmap(beforePath)) using(var after=new Bitmap(afterPath)) {
            foreach(string id in new[]{"empty-title","empty-hint","empty-sample","outline-toggle"}) {
              var rect=form.RectangleToClient(Control(form,id).RectangleToScreen(Control(form,id).ClientRectangle));
              // Compare every uncovered pixel, including antialiased glyph edges.
              rect.Width=Math.Min(rect.Width,form.nativeMenu.Left-rect.Left-1);
              int changed=Differences(before,after,rect); differences[id]=changed;
              Assert(changed==0,name+" changed "+id+" pixels: "+changed);
            }
          }
          form.CloseNativeMenu(false); form.Refresh(); await Task.Delay(50);
          string closedPath=Path.Combine(folder,name+"-closed.png"); QualityTests.Capture(form,closedPath);
          using(var before=new Bitmap(beforePath)) using(var closed=new Bitmap(closedPath)) {
            int changed=Differences(before,closed,new Rectangle(0,90,form.Width,form.Height-90));
            Assert(changed==0,name+" changed body after closing: "+changed);
          }
          checks.Add(new { language,theme,kind,openMs=clock.Elapsed.TotalMilliseconds,changedPixels=differences,browserStarted=false });
        }
        for(int i=0;i<20;i++) {
          var clock=Stopwatch.StartNew(); Button(form,i%2==0?"more":"appearance").PerformClick(); form.Update(); clock.Stop(); timings.Add(clock.Elapsed.TotalMilliseconds);
          form.CloseNativeMenu(false);
        }
        Assert(form.View==null,"Repeated menus started a browser");
        checks.Add(new { repeatedMenus=20,browserStarted=false });
        foreach(string kind in new[]{"more","appearance"}) {
          form.Size=new Size(620,480); Button(form,kind).PerformClick();
          var menu=form.nativeMenu;
          Assert(form.ClientRectangle.Contains(menu.Bounds),"Menu exceeds narrow window");
          menu.HandleNavigation(Keys.End);
          var last=menu.Content.Controls.Cast<Control>().Last(c=>c.Enabled && c.TabStop);
          Assert(last.Focused,"End did not reach the last command");
          menu.HandleNavigation(Keys.Tab);
          Assert(menu.ContainsFocus,"Tab escaped the menu");
          QualityTests.Capture(form,Path.Combine(folder,"narrow-"+kind+".png"));
          checks.Add(new { kind,narrowWindow=true,lastCommandReachable=true });
          form.CloseNativeMenu(false);
          Assert(Button(form,kind).Focused,"Closing a focused menu must return focus to its trigger");
        }
        form.Size=new Size(1120,820);
        Button(form,"more").PerformClick();
        Control(form,"language").Focus(); await InputFocusTests.Press(form,0x26);
        await Poll(()=>Task.FromResult(L.Preference=="ru"),"native language");
        Assert(form.View==null && Control(form,"empty-title").Text==L.T("Откройте или создайте файл"),"Language was not applied without a browser");
        Button(form,"theme").PerformClick(); Assert(!form.IsDark,"Native theme did not change");
        Button(form,"theme").PerformClick(); Assert(form.IsDark,"Native theme did not change back");
        form.CloseNativeMenu(false); Button(form,"appearance").PerformClick();
        for(int i=0;i<20;i++) Button(form,"font-up").PerformClick();
        Assert(!Button(form,"font-up").Enabled && form.Preferences["font-size"]=="25","Font upper limit");
        for(int i=0;i<20;i++) Button(form,"font-down").PerformClick();
        Assert(!Button(form,"font-down").Enabled && form.Preferences["font-size"]=="13","Font lower limit");
        Button(form,"font-up").PerformClick(); Button(form,"reading-full").PerformClick();
        var slider=(NativeWidthSlider)Control(form,"reading-width");
        Assert(slider.Value==100,"Full width display"); slider.Value=65;
        Assert(form.Preferences["reading-layout"]=="left" && form.Preferences["reading-width"]=="65","Width from full must select left");
        Assert(slider.AccessibilityObject.Role==AccessibleRole.Slider && slider.AccessibilityObject.Value=="65","Accessible width value");
        Button(form,"reading-center").PerformClick();
        form.CloseNativeMenu(false); await Task.Delay(450);
        var stored=Program.Json.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(Program.DataRoot,"settings.json")));
        foreach(string key in new[]{"theme","font-size","reading-layout","reading-width","language"}) Assert(stored[key]==form.Preferences[key],"Setting not persisted: "+key);
        checks.Add(new { settingsPersisted=true,fontLimits=true,accessibleSlider=true });
        var menuReferences=new Dictionary<string,Bitmap>();
        foreach(string language in new[]{"ru","en"}) foreach(string theme in new[]{"light","dark"}) {
          await form.Call("language",new object[]{language}); await form.Call("theme",new object[]{theme});
          foreach(string kind in new[]{"more","appearance"}) {
            Button(form,kind).PerformClick(); form.nativeMenu.Trigger.Focus(); form.Refresh(); await Task.Delay(60);
            string prefix=language+"-"+theme+"-"+kind;
            if(kind=="appearance") menuReferences[prefix]=MenuCapture(form,Path.Combine(folder,"empty-menu-"+prefix+".png"));
            else foreach(string id in new[]{"new-more","open-more","theme","language","help"}) menuReferences[prefix+"-"+id]=MenuCapture(form,Path.Combine(folder,"empty-menu-"+prefix+".png"),id);
            form.CloseNativeMenu(false);
          }
        }
        await form.Call("language",new object[]{"ru"});
        Button(form,"more").PerformClick(); Button(form,"new-more").PerformClick();
        await Poll(async ()=>form.UiReady && form.Core!=null && await form.Core.ExecuteScriptAsync("document.querySelectorAll('.file-tab').length===1 && !document.body.inert")=="true","create from native menu");
        string settings=await form.Core.ExecuteScriptAsync("(()=>{const s=localStorage;return s.getItem('font-size')==='14' && s.getItem('reading-width')==='65' && s.getItem('reading-layout')==='center' && document.documentElement.dataset.theme==='dark'})()");
        Assert(settings=="true","Native settings did not reach the editor");
        foreach(string language in new[]{"ru","en"}) foreach(string theme in new[]{"light","dark"}) {
          await form.Core.ExecuteScriptAsync("document.getElementById('language').value='"+language+"';document.getElementById('language').onchange()");
          await Poll(async()=>L.Language==language && await form.Core.ExecuteScriptAsync("document.documentElement.lang==='"+language+"'")=="true","document language");
          if(!form.IsDark && theme=="dark" || form.IsDark && theme=="light") await form.Core.ExecuteScriptAsync("document.getElementById('theme').click()");
          await Poll(()=>Task.FromResult(form.IsDark==(theme=="dark")),"document theme");
          foreach(string kind in new[]{"more","appearance"}) {
            await OpenDocumentMenu(form,kind);
            string prefix=language+"-"+theme+"-"+kind;
            foreach(string id in kind=="appearance"?new string[]{null}:new[]{"new-more","open-more","theme","language","help"}) {
              using(var actual=MenuCapture(form,Path.Combine(folder,"document-menu-"+prefix+".png"),id)) {
                var expected=menuReferences[prefix+(id==null?"":"-"+id)];
                Assert(actual.Size==expected.Size,"Menu dimensions differ: "+prefix+" "+id);
                int changed=Differences(expected,actual,new Rectangle(Point.Empty,actual.Size));
                Assert(changed==0,"Menu pixels differ: "+prefix+" "+id+" = "+changed);
              }
            }
            if(kind=="more") {
              Assert(Button(form,"save").Enabled && Button(form,"save-as").Enabled && !Button(form,"reload").Enabled,"Unsaved document commands");
              Assert(!Control(form,"encoding").Enabled,"Untitled encoding must be disabled");
            }
            form.CloseNativeMenu(true); await Task.Delay(50);
            checks.Add(new { language,theme,kind,emptyAndDocumentPixels="identical",htmlMenuHidden=true });
          }
        }
        foreach(var reference in menuReferences.Values) reference.Dispose();
        await OpenDocumentMenu(form,"appearance");
        Button(form,"font-up").Focus();
        await form.Core.ExecuteScriptAsync("window.menuFocusCount=0;window.addEventListener('focus',()=>menuFocusCount++)");
        Button(form,"font-up").PerformClick();
        await Poll(async()=>await form.Core.ExecuteScriptAsync("document.getElementById('font-size').textContent==='15'")=="true","native font command");
        await Poll(()=>Task.FromResult(form.nativeMenu!=null && Control(form,"font-up").Focused),"font menu refreshed");
        Assert(await form.Core.ExecuteScriptAsync("menuFocusCount===0")=="true","Refreshing menu focused the document");
        Button(form,"reading-full").PerformClick();
        await Poll(async()=>await form.Core.ExecuteScriptAsync("document.getElementById('workspace').dataset.readingLayout==='full'")=="true","native layout command");
        await Task.Delay(100);
        ((NativeWidthSlider)Control(form,"reading-width")).Value=70;
        await Poll(async()=>await form.Core.ExecuteScriptAsync("document.getElementById('reading-width').value==='70' && document.getElementById('workspace').dataset.readingLayout==='left'")=="true","native width command");
        form.CloseNativeMenu(true); await Task.Delay(50);
        await Poll(async()=>await form.Core.ExecuteScriptAsync("document.hasFocus() && document.activeElement.id==='appearance'")=="true","focus returned to menu trigger");
        await OpenDocumentMenu(form,"more"); Button(form,"view-source").PerformClick();
        await Poll(async()=>await form.Core.ExecuteScriptAsync("document.getElementById('workspace').dataset.view==='source'")=="true","native view command");
        await OpenDocumentMenu(form,"more"); form.Activate(); Assert(Button(form,"help").Focus(),"Native menu keyboard focus");
        Assert(form.NativeMenuShortcut(Keys.Control|Keys.E),"Native menu routes Ctrl+E");
        await Poll(async()=>form.nativeMenu==null && await form.Core.ExecuteScriptAsync("document.getElementById('workspace').dataset.view==='read'")=="true","shortcut from native menu");
        checks.Add(new { nativeDocumentCommands=true,font=true,layout=true,width=true,sourceMode=true });
        NativeTests.Enabled=true;
        string saved=Path.Combine(Program.DataRoot,"menu-saved.md");
        NativeTests.SavePath=saved;
        await OpenDocumentMenu(form,"more"); Button(form,"save").PerformClick();
        await Poll(async()=>File.Exists(saved) && await form.Core.ExecuteScriptAsync("document.getElementById('filename').textContent==='menu-saved.md' && !document.body.inert")=="true","native save command");
        await OpenDocumentMenu(form,"more");
        Assert(!Button(form,"save").Enabled && Button(form,"reload").Enabled && Control(form,"encoding").Enabled,"Saved file commands");
        form.CloseNativeMenu(true); await Task.Delay(50);
        File.WriteAllText(saved,"# Reloaded through menu\n");
        await OpenDocumentMenu(form,"more"); Control(form,"encoding").Focus(); await InputFocusTests.Press(form,0x24);
        await Poll(async()=>await form.Core.ExecuteScriptAsync("document.getElementById('source').value.includes('Reloaded through menu') && !document.body.inert")=="true","native encoding reload");
        NativeTests.SavePath=null;
        await OpenDocumentMenu(form,"more"); Button(form,"save-as").PerformClick();
        await Task.Delay(100);
        Assert(await form.Core.ExecuteScriptAsync("document.getElementById('filename').textContent==='menu-saved.md'")=="true","Cancelled Save As changed the document");
        await OpenDocumentMenu(form,"more"); Button(form,"pdf").PerformClick();
        await Poll(async()=>await form.Core.ExecuteScriptAsync("!document.querySelector('.tabbar').inert")=="true","cancel PDF destination");
        string second=Path.Combine(Program.DataRoot,"opened-through-menu.md"); File.WriteAllText(second,"# Opened through menu\n");
        NativeTests.OpenFiles=new[]{second};
        await OpenDocumentMenu(form,"more"); Button(form,"open-more").PerformClick();
        await Poll(async()=>await form.Core.ExecuteScriptAsync("document.querySelectorAll('.file-tab').length===2 && document.getElementById('filename').textContent==='opened-through-menu.md' && !document.body.inert")=="true","native open command");
        await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
        await Poll(async()=>await form.Core.ExecuteScriptAsync("document.querySelectorAll('.file-tab').length===1 && !document.body.inert")=="true","close opened menu fixture");
        checks.Add(new { nativeFileCommands=true,save=true,encodingReload=true,saveAsCancel=true,pdfCancel=true,open=true });
        await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
        await Poll(()=>Task.FromResult(form.View==null),"return to native surface");
        Button(form,"more").PerformClick(); Assert(form.View==null,"Menu after last tab close started browser"); form.CloseNativeMenu(false);
        checks.Add(new { nativeToEditorSettings=true,editorMenusBothThemes=true,closeLastTab=true });
        Assert(SurfaceText.FallbackCount==0,"Text renderer fallback");
        File.WriteAllText(Path.Combine(folder,"result.json"),Program.Json.Serialize(new { ok=true,dpi=form.DeviceDpi,checks,openTimingsMs=timings,profile=Program.DataRoot }));
      } catch(Exception error) { File.WriteAllText(Path.Combine(folder,"result.json"),Program.Json.Serialize(new { ok=false,error=error.ToString(),checks,openTimingsMs=timings })); }
      finally { form.CloseNativeMenu(false); form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
