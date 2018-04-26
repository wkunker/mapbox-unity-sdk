namespace Mapbox.Unity.MeshGeneration.Modifiers
{
	using System.Collections.Generic;
	using UnityEngine;
	using Mapbox.Unity.MeshGeneration.Data;
	using Mapbox.Unity.Map;
	using System;

	[CreateAssetMenu(menuName = "Mapbox/Modifiers/Textured Side Wall Modifier")]
	public class TextureSideWallModifier : MeshModifier
	{
		private float _scaledFloorHeight = 0;
		private float _scaledFirstFloorHeight = 0;
		private float _scaledTopFloorHeight = 0;
		private int _maxEdgeSectionCount = 40;
		private float _scaledPreferredWallLength;
		[SerializeField]
		private bool _centerSegments = true;
		[SerializeField]
		private bool _separateSubmesh = true;

		private List<Vector3> edgeList;
		float dist = 0;
		float step = 0;
		float dif = 0;
		Vector3 start = Constants.Math.Vector3Zero;
		Vector3 wallDirection = Constants.Math.Vector3Zero;
		Vector3 fs;
		Vector3 sc;
		float d;
		Vector3 v1;
		Vector3 v2;

		//public AtlasInfo AtlasInfo;
		private AtlasEntity _currentFacade;
		private Rect _currentTextureRect;

		private float firstHeight;
		private float topHeight;
		private float midHeight;
		private float scaledFloorHeight;
		private int ind;
		private Vector3 wallNormal;
		private List<int> wallTriangles;
		private float columnScaleRatio;
		private float rightOfEdgeUv;
		private float bottomOfTopUv;
		private float topOfBottomUv;
		private float currentY1;
		private float currentY2;
		private float topOfMidUv;
		private float _wallSizeEpsilon = 0.99f;
		private float _narrowWallWidthDelta = 0.01f;
		private float _shortRowHeightDelta = 0.015f;

		GeometryExtrusionWithAtlasOptions _options;
		private int _counter = 0;
		private float height = 0.0f;
		private float _scale = 1f;

		public override void SetProperties(ModifierProperties properties)
		{
			_options = (GeometryExtrusionWithAtlasOptions)properties;
		}

		public override void Initialize()
		{
			base.Initialize();
			edgeList = new List<Vector3>();
		}

		public override void Run(VectorFeatureUnity feature, MeshData md, UnityTile tile = null)
		{
			if (md.Vertices.Count == 0 || feature == null || feature.Points.Count < 1)
				return;

			if (tile != null)
				_scale = tile.TileScale;

			_currentFacade = _options.atlasInfo.Textures[UnityEngine.Random.Range(0, _options.atlasInfo.Textures.Count)];
			//rect is a struct so we're caching this
			_currentTextureRect = _currentFacade.TextureRect;

			//this can be moved to initialize or in an if clause if you're sure all your tiles will be same level/scale
			_scaledFloorHeight = tile.TileScale * _currentFacade.FloorHeight;
			_scaledFirstFloorHeight = tile.TileScale * _currentFacade.FirstFloorHeight;
			_scaledTopFloorHeight = tile.TileScale * _currentFacade.TopFloorHeight;
			_scaledPreferredWallLength = tile.TileScale * _currentFacade.PreferredEdgeSectionLength;

			//read or force height
			float maxHeight = 1, minHeight = 0;

			QueryHeight(feature, md, tile, out maxHeight, out minHeight);
			maxHeight = maxHeight * _options.extrusionScaleFactor * _scale;
			minHeight = minHeight * _options.extrusionScaleFactor * _scale;
			height = (maxHeight - minHeight);

			GenerateRoofMesh(md, minHeight, maxHeight);

			if (_options.extrusionGeometryType != ExtrusionGeometryType.RoofOnly)
			{
				edgeList.Clear();
				//cuts long edges into smaller ones using PreferredEdgeSectionLength
				CalculateEdgeList(md, tile, _currentFacade.PreferredEdgeSectionLength);

				//limiting section heights, first floor gets priority, then we draw top floor, then mid if we still have space
				firstHeight = Mathf.Min(height, _scaledFirstFloorHeight);
				topHeight = (height - firstHeight) < _scaledTopFloorHeight ? 0 : _scaledTopFloorHeight;
				midHeight = Mathf.Max(0, height - (firstHeight + topHeight));
				
				//scaledFloorHeight = midHeight / floorCount;
				wallTriangles = new List<int>();
				
				//this first loop is for columns
				for (int i = 0; i < edgeList.Count - 1; i += 2)
				{
					v1 = edgeList[i];
					v2 = edgeList[i + 1];
					ind = md.Vertices.Count;
					wallDirection = (v2 - v1);
					d = wallDirection.magnitude;

					//this part minimizes stretching for narrow columns
					//if texture has 3 columns, 33% (of preferred edge length) wide walls will get 1 window.
					//0-33% gets 1 window, 33-66 gets 2, 66-100 gets all three
					//we're not wrapping/repeating texture as it won't work with atlases
					columnScaleRatio = Math.Min(1, d / _scaledPreferredWallLength);
					rightOfEdgeUv = _currentTextureRect.xMin + _currentTextureRect.size.x * columnScaleRatio; // Math.Min(1, ((float)(Math.Floor(columnScaleRatio * _currentFacade.ColumnCount) + 1) / _currentFacade.ColumnCount));
					bottomOfTopUv = _currentTextureRect.yMax - (_currentTextureRect.size.y * _currentFacade.TopSectionRatio); //not doing that scaling thing for y axis and floors yet
					topOfBottomUv = _currentTextureRect.yMin + (_currentTextureRect.size.y * _currentFacade.BottomSectionRatio); // * (Mathf.Max(1, (float)Math.Floor(tby * textureSection.TopSectionFloorCount)) / textureSection.TopSectionFloorCount);

					//common for all top/mid/bottom segments
					wallNormal = new Vector3(-(v1.z - v2.z), 0, (v1.x - v2.x)).normalized;
					//height of the left/right edges
					currentY1 = v1.y;
					currentY2 = v2.y;
					
					if(feature.Data.Id == 1487108801)
					{
						Debug.Log("here");
					}

					FirstFloor(md, height);
					TopFloor(md);
					MidFloors(md);
				}

				if (_separateSubmesh)
				{
					md.Triangles.Add(wallTriangles);
				}
				else
				{
					md.Triangles.Capacity = md.Triangles.Count + wallTriangles.Count;
					md.Triangles[0].AddRange(wallTriangles);
				}
			}
		}

		private void MidFloors(MeshData md)
		{
			var leftOver = midHeight;
			var singleFloorHeight = _scaledFloorHeight / _currentFacade.MidFloorCount;

			topOfMidUv = _currentTextureRect.yMax - (_currentTextureRect.height * _currentFacade.TopSectionRatio);
			var midUvHeight = _currentTextureRect.height * (1 - _currentFacade.TopSectionRatio - _currentFacade.BottomSectionRatio);
			scaledFloorHeight = _scaledPreferredWallLength * (1 - _currentFacade.TopSectionRatio - _currentFacade.BottomSectionRatio) * (_currentTextureRect.height / _currentTextureRect.width);

			while (leftOver >= singleFloorHeight - 0.01f)
			{
				var singleFloorCount = (float)Math.Min(_currentFacade.MidFloorCount, Math.Floor(leftOver / singleFloorHeight));
				var stepRatio = singleFloorCount / _currentFacade.MidFloorCount;

				//top two vertices
				md.Vertices.Add(new Vector3(v1.x, currentY1, v1.z));
				md.Vertices.Add(new Vector3(v2.x, currentY2, v2.z));
				//move offsets bottom
				currentY1 -= (scaledFloorHeight * stepRatio);
				currentY2 -= (scaledFloorHeight * stepRatio);
				//bottom two vertices
				md.Vertices.Add(new Vector3(v1.x, currentY1, v1.z));
				md.Vertices.Add(new Vector3(v2.x, currentY2, v2.z));
				
				if (d >= (_scaledPreferredWallLength / _currentFacade.ColumnCount) * _wallSizeEpsilon)
				{
					md.UV[0].Add(new Vector2(_currentTextureRect.xMin, topOfMidUv));
					md.UV[0].Add(new Vector2(rightOfEdgeUv, topOfMidUv));
					md.UV[0].Add(new Vector2(_currentTextureRect.xMin, topOfMidUv - midUvHeight * stepRatio));
					md.UV[0].Add(new Vector2(rightOfEdgeUv, topOfMidUv - midUvHeight * stepRatio));
				}
				else
				{
					md.UV[0].Add(new Vector2(_currentTextureRect.xMin, topOfMidUv));
					md.UV[0].Add(new Vector2(_currentTextureRect.xMin + _narrowWallWidthDelta, topOfMidUv));
					md.UV[0].Add(new Vector2(_currentTextureRect.xMin, topOfMidUv - midUvHeight * stepRatio));
					md.UV[0].Add(new Vector2(_currentTextureRect.xMin + _narrowWallWidthDelta, topOfMidUv - midUvHeight * stepRatio));
				}

				md.Normals.Add(wallNormal);
				md.Normals.Add(wallNormal);
				md.Normals.Add(wallNormal);
				md.Normals.Add(wallNormal);

				md.Tangents.Add(wallDirection);
				md.Tangents.Add(wallDirection);
				md.Tangents.Add(wallDirection);
				md.Tangents.Add(wallDirection);

				wallTriangles.Add(ind);
				wallTriangles.Add(ind + 1);
				wallTriangles.Add(ind + 2);

				wallTriangles.Add(ind + 1);
				wallTriangles.Add(ind + 3);
				wallTriangles.Add(ind + 2);

				ind += 4;
				leftOver -= Math.Max(0.1f, (scaledFloorHeight * stepRatio));
			}
		}

		private void TopFloor(MeshData md)
		{
			var leftOver = midHeight > 0 ? midHeight : topHeight;
			var singleFloorHeight = _scaledFloorHeight / _currentFacade.MidFloorCount;
			leftOver = leftOver % singleFloorHeight;

			//leftover
			md.Vertices.Add(new Vector3(v1.x, currentY1, v1.z));
			md.Vertices.Add(new Vector3(v2.x, currentY2, v2.z));
			//move offsets bottom
			currentY1 -= leftOver;
			currentY2 -= leftOver;
			//bottom two vertices
			md.Vertices.Add(new Vector3(v1.x, currentY1, v1.z));
			md.Vertices.Add(new Vector3(v2.x, currentY2, v2.z));

			if (d >= (_scaledPreferredWallLength / _currentFacade.ColumnCount) * _wallSizeEpsilon)
			{
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, _currentTextureRect.yMax));
				md.UV[0].Add(new Vector2(rightOfEdgeUv, _currentTextureRect.yMax));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, _currentTextureRect.yMax - _shortRowHeightDelta));
				md.UV[0].Add(new Vector2(rightOfEdgeUv, _currentTextureRect.yMax - _shortRowHeightDelta));
			}
			else
			{
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, _currentTextureRect.yMax));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin + _narrowWallWidthDelta, _currentTextureRect.yMax));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, _currentTextureRect.yMax - _shortRowHeightDelta));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin + _narrowWallWidthDelta, _currentTextureRect.yMax - _shortRowHeightDelta));
			}			

			md.Normals.Add(wallNormal);
			md.Normals.Add(wallNormal);
			md.Normals.Add(wallNormal);
			md.Normals.Add(wallNormal);

			md.Tangents.Add(wallDirection);
			md.Tangents.Add(wallDirection);
			md.Tangents.Add(wallDirection);
			md.Tangents.Add(wallDirection);

			wallTriangles.Add(ind);
			wallTriangles.Add(ind + 1);
			wallTriangles.Add(ind + 2);

			wallTriangles.Add(ind + 1);
			wallTriangles.Add(ind + 3);
			wallTriangles.Add(ind + 2);

			ind += 4;

			//top floor start
			currentY1 -= topHeight;
			currentY2 -= topHeight;
			md.Vertices.Add(new Vector3(v1.x, v1.y - leftOver, v1.z));
			md.Vertices.Add(new Vector3(v2.x, v2.y - leftOver, v2.z));
			md.Vertices.Add(new Vector3(v1.x, v1.y - leftOver - topHeight, v1.z));
			md.Vertices.Add(new Vector3(v2.x, v2.y - leftOver - topHeight, v2.z));

			if (d >= (_scaledPreferredWallLength / _currentFacade.ColumnCount) * _wallSizeEpsilon)
			{
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, _currentTextureRect.yMax));
				md.UV[0].Add(new Vector2(rightOfEdgeUv, _currentTextureRect.yMax));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, bottomOfTopUv));
				md.UV[0].Add(new Vector2(rightOfEdgeUv, bottomOfTopUv));
			}
			else
			{
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, _currentTextureRect.yMax));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin + _narrowWallWidthDelta, _currentTextureRect.yMax));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, bottomOfTopUv));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin + _narrowWallWidthDelta, bottomOfTopUv));
			}

			md.Normals.Add(wallNormal);
			md.Normals.Add(wallNormal);
			md.Normals.Add(wallNormal);
			md.Normals.Add(wallNormal);


			md.Tangents.Add(wallDirection);
			md.Tangents.Add(wallDirection);
			md.Tangents.Add(wallDirection);
			md.Tangents.Add(wallDirection);

			wallTriangles.Add(ind);
			wallTriangles.Add(ind + 1);
			wallTriangles.Add(ind + 2);

			wallTriangles.Add(ind + 1);
			wallTriangles.Add(ind + 3);
			wallTriangles.Add(ind + 2);

			ind += 4;
		}

		private void FirstFloor(MeshData md, float hf)
		{
			md.Vertices.Add(new Vector3(v1.x, v1.y - hf + firstHeight, v1.z));
			md.Vertices.Add(new Vector3(v2.x, v2.y - hf + firstHeight, v2.z));
			md.Vertices.Add(new Vector3(v1.x, v1.y - hf, v1.z));
			md.Vertices.Add(new Vector3(v2.x, v2.y - hf, v2.z));

			md.Normals.Add(wallNormal);
			md.Normals.Add(wallNormal);
			md.Normals.Add(wallNormal);
			md.Normals.Add(wallNormal);
			md.Tangents.Add(wallDirection);
			md.Tangents.Add(wallDirection);
			md.Tangents.Add(wallDirection);
			md.Tangents.Add(wallDirection);

			if (d >= (_scaledPreferredWallLength / _currentFacade.ColumnCount) * _wallSizeEpsilon)
			{
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, topOfBottomUv));
				md.UV[0].Add(new Vector2(rightOfEdgeUv, topOfBottomUv));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, _currentTextureRect.yMin));
				md.UV[0].Add(new Vector2(rightOfEdgeUv, _currentTextureRect.yMin));
			}
			else
			{
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, topOfBottomUv));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin + _narrowWallWidthDelta, topOfBottomUv));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin, _currentTextureRect.yMin));
				md.UV[0].Add(new Vector2(_currentTextureRect.xMin + _narrowWallWidthDelta, _currentTextureRect.yMin));
			}

			wallTriangles.Add(ind);
			wallTriangles.Add(ind + 1);
			wallTriangles.Add(ind + 2);

			wallTriangles.Add(ind + 1);
			wallTriangles.Add(ind + 3);
			wallTriangles.Add(ind + 2);

			ind += 4;
		}

		private void CalculateEdgeList(MeshData md, UnityTile tile, float preferredEdgeSectionLength)
		{
			dist = 0;
			step = 0;
			dif = 0;
			start = Constants.Math.Vector3Zero;
			wallDirection = Constants.Math.Vector3Zero;
			for (int i = 0; i < md.Edges.Count; i += 2)
			{
				fs = md.Vertices[md.Edges[i]];
				sc = md.Vertices[md.Edges[i + 1]];

				dist = Vector3.Distance(fs, sc);
				var singleColumn = _scaledPreferredWallLength / _currentFacade.ColumnCount;

				var leftOver = dist % singleColumn;
				var currentWall = dist;
				start = fs;
				wallDirection = (sc - fs).normalized;

				if (_centerSegments && currentWall > singleColumn)
				{
					edgeList.Add(start);
					start = start + wallDirection * (leftOver / 2);
					edgeList.Add(start);
					leftOver = leftOver / 2;
				}
								
				while(currentWall > singleColumn)
				{
					edgeList.Add(start);
					var singleColumnCount = (float)Math.Min(_currentFacade.ColumnCount, Math.Floor(currentWall / singleColumn));
					var stepRatio = singleColumnCount / _currentFacade.ColumnCount;
					start = start + wallDirection * (stepRatio * _scaledPreferredWallLength);
					edgeList.Add(start);
					currentWall -= (stepRatio * _scaledPreferredWallLength);
				}

				if(leftOver > 0)
				{
					edgeList.Add(start);
					edgeList.Add(sc);
				}

				//step = Mathf.Min(_maxEdgeSectionCount, dist / _scaledPreferredWallLength);

				//edgeList.Add(start);
				//if (_centerSegments && step > 1)
				//{
				//	dif = dist - ((int)step * _scaledPreferredWallLength);
				//	//prevent new point being to close to existing corner
				//	if (dif > 2 * tile.TileScale)
				//	{
				//		//first step, original point or another close point if sections are centered
				//		start = fs + (wallDirection * (dif / 2));
				//		//to compansate step-1 below, so if there's more than 2m to corner, go one more step
				//	}
				//	edgeList.Add(start);
				//	edgeList.Add(start);
				//}
				//if (step > 1)
				//{
				//	for (int s = 1; s < step; s++)
				//	{
				//		var da = start + wallDirection * s * _scaledPreferredWallLength;
				//		edgeList.Add(da);
				//		edgeList.Add(da);
				//	}
				//}

				//edgeList.Add(sc);
			}
		}

		private void GenerateRoofMesh(MeshData md, float minHeight, float maxHeight)
		{
			if (_options.extrusionGeometryType != ExtrusionGeometryType.SideOnly)
			{
				_counter = md.Vertices.Count;
				switch (_options.extrusionType)
				{
					case ExtrusionType.None:
						break;
					case ExtrusionType.PropertyHeight:
						for (int i = 0; i < _counter; i++)
						{
							md.Vertices[i] = new Vector3(md.Vertices[i].x, md.Vertices[i].y + maxHeight, md.Vertices[i].z);
						}
						break;
					case ExtrusionType.MinHeight:
						{
							var minmax = MinMaxPair.GetMinMaxHeight(md.Vertices);
							for (int i = 0; i < _counter; i++)
							{
								md.Vertices[i] = new Vector3(md.Vertices[i].x, minmax.min + maxHeight, md.Vertices[i].z);
							}
						}
						//hf += max - min;
						break;
					case ExtrusionType.MaxHeight:
						{
							var minmax = MinMaxPair.GetMinMaxHeight(md.Vertices);
							for (int i = 0; i < _counter; i++)
							{
								md.Vertices[i] = new Vector3(md.Vertices[i].x, minmax.max + maxHeight, md.Vertices[i].z);
							}
							height += minmax.max - minmax.min;
						}
						break;
					case ExtrusionType.RangeHeight:
						for (int i = 0; i < _counter; i++)
						{
							md.Vertices[i] = new Vector3(md.Vertices[i].x, md.Vertices[i].y + maxHeight, md.Vertices[i].z);
						}
						break;
					case ExtrusionType.AbsoluteHeight:
						for (int i = 0; i < _counter; i++)
						{
							md.Vertices[i] = new Vector3(md.Vertices[i].x, maxHeight, md.Vertices[i].z);
						}
						break;
					default:
						break;
				}
			}
		}

		private void QueryHeight(VectorFeatureUnity feature, MeshData md, UnityTile tile, out float maxHeight, out float minHeight)
		{
			minHeight = 0.0f;
			maxHeight = 0.0f;

			switch (_options.extrusionType)
			{
				case ExtrusionType.None:
					break;
				case ExtrusionType.PropertyHeight:
				case ExtrusionType.MinHeight:
				case ExtrusionType.MaxHeight:
					if (feature.Properties.ContainsKey(_options.propertyName))
					{
						maxHeight = Convert.ToSingle(feature.Properties[_options.propertyName]);
						if (feature.Properties.ContainsKey("min_height"))
						{
							minHeight = Convert.ToSingle(feature.Properties["min_height"]);
							//hf -= minHeight;
						}
					}
					break;
				case ExtrusionType.RangeHeight:
					if (feature.Properties.ContainsKey(_options.propertyName))
					{
						if (_options.minimumHeight > _options.maximumHeight)
						{
							Debug.LogError("Maximum Height less than Minimum Height.Swapping values for extrusion.");
							var temp = _options.minimumHeight;
							_options.minimumHeight = _options.maximumHeight;
							_options.maximumHeight = temp;
						}
						var featureHeight = Convert.ToSingle(feature.Properties[_options.propertyName]);
						maxHeight = Math.Min(Math.Max(_options.minimumHeight, featureHeight), _options.maximumHeight);
						if (feature.Properties.ContainsKey("min_height"))
						{
							var featureMinHeight = Convert.ToSingle(feature.Properties["min_height"]);
							minHeight = Math.Min(featureMinHeight, _options.maximumHeight);
							//maxHeight -= minHeight;
						}
					}
					break;
				case ExtrusionType.AbsoluteHeight:
					maxHeight = _options.maximumHeight;
					break;
				default:
					break;
			}
		}
	}
}
