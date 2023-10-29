using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shapes {
	
	[ExecuteAlways]
	public class FpsController : ImmediateModeShapeDrawer {

		// components
		public Transform head;
		public Camera cam;
		public Compass compass;
		public Transform crosshairTransform;

		// called by the ImmediateModeShapeDrawer base type
		public override void DrawShapes( Camera cam ) {
			if( cam != this.cam ) // only draw in the player camera
				return;

			using( Draw.Command( cam ) ) {
				Draw.ZTest = CompareFunction.Always; // to make sure it draws on top of everything like a HUD
				Draw.Matrix = crosshairTransform.localToWorldMatrix; // draw it in the space of crosshairTransform
				Draw.BlendMode = ShapesBlendMode.Transparent;
				Draw.LineGeometry = LineGeometry.Flat2D;
				compass.DrawCompass( this.cam.transform.forward );
			}
		}
		

		
	}

}