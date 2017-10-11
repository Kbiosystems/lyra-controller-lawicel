using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LyraElectronics.Controller
{
    public class ControllerMessageRecievedEventArgs : EventArgs
    {
        public byte[] Data { get; private set; }

        public ControllerMessageRecievedEventArgs(byte[] data)
        {
            Data = data;
        }
    }
}
