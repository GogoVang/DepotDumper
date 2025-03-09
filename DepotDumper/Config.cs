// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

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
