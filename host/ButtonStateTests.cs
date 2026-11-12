#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class ButtonStateTests {
    static readonly string[] Ids={"open","new","appearance","more"};
    static Control Native(ReaderForm form,string id) { return QualityTests.Descendants(form).Single(c=>id=="open"?c is PlusButton:id=="new"?c is ChromeLabelButton && c.Text==L.T("Создать"):c.Name==id); }
    static async Task<Rectangle> Bounds(ReaderForm form,string id,bool web) {
      if(!web) {var c=Native(form,id);return form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));}
      var r=await InputFocusTests.JS<int[]>(form,"(()=>{const r=document.getElementById('"+id+"').getBoundingClientRect();return [r.x,r.y,r.width,r.height].map(v=>Math.round(v*devicePixelRatio))})()");
      return new Rectangle(r[0],r[1],r[2],r[3]);
    }
    static async Task FocusByTab(ReaderForm form,string id,bool web) {
      for(int i=0;i<45;i++) {
        await InputFocusTests.Press(form,9);
        if(web ? await InputFocusTests.JS<bool>(form,"document.activeElement.id==='"+id+"' && document.activeElement.matches(':focus-visible')") : Native(form,id).Focused) return;
      }
      throw new Exception("Tab did not reach "+id+" web="+web);
    }
    static void Save(ReaderForm form,Rectangle rect,string path) {
      using(var full=new Bitmap(form.Width,form.Height)) {
        using(var g=Graphics.FromImage(full)) g.CopyFromScreen(form.Location,Point.Empty,form.Size);
        using(var crop=full.Clone(rect,full.PixelFormat)) crop.Save(path);
      }
    }
    static int Delta(string first,string second) {
      using(var a=new Bitmap(first)) using(var b=new Bitmap(second)) {
        if(a.Size!=b.Size) return 255;
        int max=0;
        for(int y=0;y<a.Height;y++) for(int x=0;x<a.Width;x++) { var p=a.GetPixel(x,y);var q=b.GetPixel(x,y); max=Math.Max(max,Math.Max(Math.Abs(p.R-q.R),Math.Max(Math.Abs(p.G-q.G),Math.Abs(p.B-q.B)))); }
        return max;
      }
    }
    internal static async Task Run(ReaderForm form) {
      string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"button-state-checks"); Directory.CreateDirectory(folder);
      var checks=new List<object>(); var failures=new List<string>();
      try {
        form.TopMost=true; form.Show(); form.Activate(); form.Size=new Size(1120,820);
        await form.Call("language",new object[]{"ru"});
        var file=Path.Combine(Program.DataRoot,"Buttons.md");File.WriteAllText(file,"# Buttons\n\nA saved document for control-state checks.\n");
        foreach(string theme in new[]{"light","dark"}) {
          await form.Call("theme",new object[]{theme}); await Task.Delay(100);
          foreach(bool web in new[]{false,true}) {
            if(web) {
              form.ReceiveArguments(new[]{file});
              for(int i=0;i<600 && !form.UiReady;i++) await Task.Delay(50);
              if(!form.UiReady) throw new Exception("Document not ready");
              await Task.Delay(200);
            } else if(form.Core!=null) throw new Exception("Empty state started browser");
            var away=form.PointToScreen(new Point(600,500));
            foreach(string id in Ids) {
              var bounds=await Bounds(form,id,web);var point=form.PointToScreen(new Point(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2));
              string prefix=Path.Combine(folder,theme+"-"+(web?"document":"empty")+"-"+id+"-");
              await InputFocusTests.Click(form,away);
              InputFocusTests.Move(form,point); await Task.Delay(100);
              if(web && !await InputFocusTests.JS<bool>(form,"document.getElementById('"+id+"').matches(':hover')")) throw new Exception("Initial hover input did not reach "+id);
              Save(form,bounds,prefix+"hover.png");
              InputFocusTests.MouseButton(true);
              try { await Task.Delay(100);
                if(web && !await InputFocusTests.JS<bool>(form,"document.getElementById('"+id+"').matches(':active')")) throw new Exception("Pointer down did not activate "+id);
                Save(form,bounds,prefix+"pressed.png"); InputFocusTests.Move(form,away); await Task.Delay(50); }
              finally {InputFocusTests.MouseButton(false);}
              await FocusByTab(form,id,web); await Task.Delay(80); Save(form,bounds,prefix+"focus.png");
              InputFocusTests.Move(form,form.PointToScreen(new Point(bounds.X+2,bounds.Y+2)));await Task.Delay(80);
              InputFocusTests.Move(form,point); await Task.Delay(180);
              if(web && !await InputFocusTests.JS<bool>(form,"document.getElementById('"+id+"').matches(':hover')")) {
                QualityTests.Capture(form,Path.Combine(folder,"hover-failure.png"));
                throw new Exception("Hover input did not reach "+id+" point="+point+" cursor="+Cursor.Position+" original="+bounds+" current="+await Bounds(form,id,true)+" hovered="+await form.Core.ExecuteScriptAsync("[...document.querySelectorAll(':hover')].map(e=>e.id||e.tagName)"));
              }
              Save(form,bounds,prefix+"focus-hover.png");
              if(web) foreach(string state in new[]{"hover","pressed","focus","focus-hover"}) {
                int max=Delta(Path.Combine(folder,theme+"-empty-"+id+"-"+state+".png"),prefix+state+".png");
                checks.Add(new{theme,id,state,maxDelta=max}); if(max>2) failures.Add(theme+" "+id+" "+state+": "+max);
              }
            }
            InputFocusTests.Move(form,away);
            foreach(string id in new[]{"appearance","more"}) {
              var bounds=await Bounds(form,id,web);
              await InputFocusTests.Click(form,form.PointToScreen(new Point(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2)));
              InputFocusTests.Move(form,away); await Task.Delay(100);
              await InputFocusTests.Press(form,9);
              if(form.nativeMenu==null || !form.nativeMenu.ContainsFocus) throw new Exception("Tab did not enter "+id);
              await InputFocusTests.Press(form,27); await Task.Delay(150);
              if(form.nativeMenu!=null) throw new Exception("Escape did not close "+id);
              string prefix=Path.Combine(folder,theme+"-"+(web?"document":"empty")+"-"+id+"-");
              Save(form,bounds,prefix+"return-focus.png");
              int max=Delta(prefix+"focus.png",prefix+"return-focus.png");
              string state=web?await form.Core.ExecuteScriptAsync("[document.activeElement.id,document.activeElement.matches(':focus-visible')]"):Native(form,id).Focused.ToString();
              checks.Add(new{theme,id,state="return-focus",web,maxDelta=max,focus=state});
              if(max>2) failures.Add(theme+" "+id+" return-focus web="+web+": "+max+" "+state);
              await InputFocusTests.Click(form,form.PointToScreen(new Point(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2)));
              bool cleared=web?await InputFocusTests.JS<bool>(form,"!document.getElementById('"+id+"').classList.contains('native-keyboard-focus')"):!QuietButton.KeyboardFocus;
              checks.Add(new{theme,id,state="pointer-clears-keyboard-focus",web,ok=cleared});
              if(!cleared) failures.Add(theme+" "+id+" retained keyboard frame after pointer click");
              await InputFocusTests.Click(form,away);
            }
          }
          foreach(string closeWith in new[]{"pointer","menu-pointer","keyboard"}) {
            bool keyboard=closeWith=="keyboard";
            if(closeWith!="pointer") {
              form.ReceiveArguments(new[]{file});
              for(int i=0;i<600 && !form.UiReady;i++) await Task.Delay(50);
              if(!form.UiReady) throw new Exception("Document not ready for keyboard close");
            }
            await FocusByTab(form,"more",true);
            if(closeWith=="menu-pointer") { await InputFocusTests.Press(form,13); await InputFocusTests.Press(form,9); if(form.nativeMenu==null || !form.nativeMenu.ContainsFocus) throw new Exception("Menu did not take focus before tab close"); }
            if(keyboard) await InputFocusTests.Press(form,0x57,true);
            else {
              var xy=await InputFocusTests.JS<int[]>(form,"(()=>{const r=document.querySelector('.tab-close').getBoundingClientRect();return [r.x+r.width/2,r.y+r.height/2].map(v=>Math.round(v*devicePixelRatio))})()");
              await InputFocusTests.Click(form,form.PointToScreen(new Point(xy[0],xy[1])));
            }
            for(int i=0;i<600 && form.Core!=null;i++) await Task.Delay(30);
            if(form.Core!=null) throw new Exception("Last tab did not close: keyboard="+keyboard);
            bool ok=QuietButton.KeyboardFocus==keyboard;
            checks.Add(new{theme,state="last-tab-focus",closeWith,keyboard,ok});
            if(!ok) failures.Add(theme+" last tab retained wrong input modality: keyboard="+keyboard);
            QualityTests.Capture(form,Path.Combine(folder,theme+"-last-tab-"+closeWith+".png"));
          }
        }
        File.WriteAllText(Path.Combine(folder,"result.json"),Program.Json.Serialize(new{ok=failures.Count==0,checks,failures,method="Existing SendInput helper: hover, held mouse, physical Tab; isolated profile",dpi=form.DeviceDpi}));
      } catch(Exception error) {File.WriteAllText(Path.Combine(folder,"result.json"),Program.Json.Serialize(new{ok=false,error=error.ToString(),checks,failures}));}
      finally {InputFocusTests.MouseButton(false);form.ReleaseBrowser();form.Close();}
    }
  }
}
#endif
