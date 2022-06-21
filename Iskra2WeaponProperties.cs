using System.Collections.Generic;
using UnityEngine;
using Receiver2;

namespace Iskra2Patch {
	class Iskra2WeaponProperties : MonoBehaviour{
		public Plugin.BoltState bolt_state;

		public bool pullingStriker;
		public bool decocking;
		public bool press_check;
		public SimpleMagazineScript magazine;

		public LinearMover bolt = new LinearMover();
		public RotateMover bolt_lock = new RotateMover();
		public LinearMover striker = new LinearMover();

		public List<SightAttachment> sights = new List<SightAttachment>();
		public SightAttachment currentSight;
	}
}
