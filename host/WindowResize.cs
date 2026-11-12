using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MdReader {
  sealed partial class ReaderForm {
    int ResizeHit(Point point) {
      if (WindowState != FormWindowState.Normal || !ClientRectangle.Contains(point)) return 1;
      int border = Math.Max(1, (6 * DeviceDpi + 48) / 96);
      bool left = point.X < border, right = point.X >= ClientSize.Width - border;
      bool top = point.Y < border, bottom = point.Y >= ClientSize.Height - border;
      return top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 1;
    }

    sealed class ResizeInputFilter : IMessageFilter {
      readonly ReaderForm form;
      [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
      [DllImport("user32")] static extern bool ClientToScreen(IntPtr window, ref Point point);
      internal ResizeInputFilter(ReaderForm owner) { form = owner; }
      public bool PreFilterMessage(ref Message message) {
        if (message.Msg != 0x200 && message.Msg != 0x201) return false;
        if (form.IsDisposed || form.WindowState != FormWindowState.Normal || GetAncestor(message.HWnd, 2) != form.Handle) return false;
        long position = message.LParam.ToInt64();
        var screen = new Point((short)(position & 0xFFFF), (short)((position >> 16) & 0xFFFF));
        if (!ClientToScreen(message.HWnd, ref screen)) return false;
        int hit = form.ResizeHit(form.PointToClient(screen));
        if (hit == 1) return false;
        if (message.Msg == 0x200) {
          if (Control.MouseButtons != MouseButtons.None) return false;
          Cursor.Current = hit == 10 || hit == 11 ? Cursors.SizeWE : hit == 12 || hit == 15 ? Cursors.SizeNS :
            hit == 13 || hit == 17 ? Cursors.SizeNWSE : Cursors.SizeNESW;
        } else {
          // WebView2 child windows receive border clicks before the form's hit test.
          // Hand only these clicks to Windows; it owns capture and the sizing loop.
          ReleaseCapture();
          SendMessage(form.Handle, 0xA1, new IntPtr(hit), new IntPtr((screen.Y << 16) | (screen.X & 0xFFFF)));
        }
        return true;
      }
    }
  }
}
