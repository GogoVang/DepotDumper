using System;
using System.Collections.Generic;
using System.Text;

namespace DepotDumper
{
    class DumperConfig
    {
        public bool RememberPassword { get; set; }
        public bool DumpUnreleased { get; set; }
        public uint TargetAppId { get; set; }
        public bool UseQrCode { get; set; }
    }
}
