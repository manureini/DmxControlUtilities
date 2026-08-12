using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DmxControlUtilities.Lib.Models
{
    public class ItemListEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Xml { get; set; } = string.Empty;
    }
}
