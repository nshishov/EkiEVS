using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EkiEVS
{
	class Sortable<T>
	{
		public int SortOrder { get; set; }
		public T Object { get; set; }
	}
}
