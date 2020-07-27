/*
 * Copyright (C) 2019 HERE Europe B.V.
 * Licensed under MIT, see full license in LICENSE
 * SPDX-License-Identifier: MIT
 * License-Filename: LICENSE
 */

using System;
using System.Collections.Generic;
using Java.Util.Concurrent.Atomic;

namespace com.here.flexpolyline
{
	public class PolylineDecoder
	{
		/**
		 * Header version
		 * A change in the version may affect the logic to encode and decode the rest of the header and data
		*/
		public const sbyte FORMAT_VERSION = 1;

		//Base64 URL-safe characters
		public static readonly int[] DECODING_TABLE = new int[] { 62, -1, -1, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, -1, -1, -1, -1, -1, -1, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, -1, -1, -1, -1, 63, -1, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51 };
		
		/// Decode the encoded input <seealso cref="string"/> to <seealso cref="System.Collections.IList"/> of coordinate triples.<BR><BR> </summary>
		/// <param name="encoded"> URL-safe encoded <seealso cref="string"/> </param>
		/// <returns> <seealso cref="System.Collections.IList"/> of coordinate triples that are decoded from input
		/// </returns>
		/// <seealso cref= PolylineDecoder#getThirdDimension(String) getThirdDimension </seealso>
		/// <seealso cref= LatLngZ </seealso>
		public static IList<LatLngZ> decode(string encoded)
		{

			if (string.ReferenceEquals(encoded, null) || encoded.Trim().Length == 0)
			{
				throw new System.ArgumentException("Invalid argument!");
			}
			IList<LatLngZ> result = new List<LatLngZ>();
			Decoder dec = new Decoder(encoded);
			AtomicReference lat = new AtomicReference(0d);
			AtomicReference lng = new AtomicReference(0d);
			AtomicReference z = new AtomicReference(0d);

			while (dec.decodeOne(lat, lng, z))
			{
				result.Add(new LatLngZ(Convert.ToDouble(lat.Get()), Convert.ToDouble(lng.Get()), Convert.ToDouble(z.Get())));
				lat = new AtomicReference(0d);
				lng = new AtomicReference(0d);
				z = new AtomicReference(0d);
			}
			return result;
		}

		/// <summary>
		/// ThirdDimension type from the encoded input <seealso cref="string"/> </summary>
		/// <param name="encoded"> URL-safe encoded coordinate triples <seealso cref="string"/> </param>
		/// <returns> type of <seealso cref="ThirdDimension"/> </returns>
		public static ThirdDimension getThirdDimension(string encoded)
		{
			AtomicInteger index = new AtomicInteger(0);
			AtomicLong header = new AtomicLong(0);
			Decoder.decodeHeaderFromString(encoded, index, header);
			return ThirdDimension.fromNum((header.Get() >> 4) & 7);
		}
		public virtual sbyte Version
		{
			get
			{
				return FORMAT_VERSION;
			}
		}
		/*
		 * Single instance for decoding an input request. 
		 */
		private class Decoder
		{

			internal readonly string encoded;
			internal readonly AtomicInteger index;
			internal readonly Converter latConveter;
			internal readonly Converter lngConveter;
			internal readonly Converter zConveter;

			internal int precision;
			internal int thirdDimPrecision;
			internal ThirdDimension thirdDimension;

			public Decoder(string encoded)
			{
				this.encoded = encoded;
				this.index = new AtomicInteger(0);
				decodeHeader();
				this.latConveter = new Converter(precision);
				this.lngConveter = new Converter(precision);
				this.zConveter = new Converter(thirdDimPrecision);
			}

			internal virtual bool hasThirdDimension()
			{
				return thirdDimension != ThirdDimension.ABSENT;
			}

			internal virtual void decodeHeader()
			{
				AtomicLong header = new AtomicLong(0);
				decodeHeaderFromString(encoded, index, header);
				precision = (int)(header.Get() & 15); // we pick the first 4 bits only
				header.Set(header.Get() >> 4);
				thirdDimension = ThirdDimension.fromNum(header.Get() & 7); // we pick the first 3 bits only
				thirdDimPrecision = (int)((header.Get() >> 3) & 15);
			}

			internal static void decodeHeaderFromString(string encoded, AtomicInteger index, AtomicLong header)
			{
				AtomicLong value = new AtomicLong(0);

				// Decode the header version
				if (!Converter.decodeUnsignedVarint(encoded.ToCharArray(), index, value))
				{
					throw new System.ArgumentException("Invalid encoding");
				}
				if (value.Get() != FORMAT_VERSION)
				{
					throw new System.ArgumentException("Invalid format version");
				}
				// Decode the polyline header
				if (!Converter.decodeUnsignedVarint(encoded.ToCharArray(), index, value))
				{
					throw new System.ArgumentException("Invalid encoding");
				}
				header.Set(value.Get());
			}
			internal virtual bool decodeOne(AtomicReference lat, AtomicReference lng, AtomicReference z)
			{
				if (index.Get() == encoded.Length)
				{
					return false;
				}
				if (!latConveter.decodeValue(encoded, index, lat))
				{
					throw new System.ArgumentException("Invalid encoding");
				}
				if (!lngConveter.decodeValue(encoded, index, lng))
				{
					throw new System.ArgumentException("Invalid encoding");
				}
				if (hasThirdDimension())
				{
					if (!zConveter.decodeValue(encoded, index, z))
					{
						throw new System.ArgumentException("Invalid encoding");
					}
				}
				return true;
			}
		}
		//Decode a single char to the corresponding value
		private static int decodeChar(char charValue)
		{
			int pos = charValue - 45;
			if (pos < 0 || pos > 77)
			{
				return -1;
			}
			return DECODING_TABLE[pos];
		}
		/*
		 * Stateful instance for decoding on a sequence of Coordinates part of an request.
		 * Instance should be specific to type of coordinates (e.g. Lat, Lng)
		 * so that specific type delta is computed for encoding.
		 * Lat0 Lng0 3rd0 (Lat1-Lat0) (Lng1-Lng0) (3rdDim1-3rdDim0)   
		 */
		public class Converter
		{

			internal long multiplier = 0;
			internal long lastValue = 0;

			public Converter(int precision)
			{
				Precision = precision;
			}
			internal virtual int Precision
			{
				set
				{
					multiplier = (long)Math.Pow(10, Convert.ToDouble(value));
				}
			}
			internal static bool decodeUnsignedVarint(char[] encoded, AtomicInteger index, AtomicLong result)
			{
				short shift = 0;
				long delta = 0;
				long value;

				while (index.Get() < encoded.Length)
				{
					value = decodeChar(encoded[index.Get()]);
					if (value < 0)
					{
						return false;
					}
					index.IncrementAndGet();
					delta |= (value & 0x1F) << shift;
					if ((value & 0x20) == 0)
					{
						result.Set(delta);
						return true;
					}
					else
					{
						shift += 5;
					}
				}

				if (shift > 0)
				{
					return false;
				}
				return true;
			}
			//Decode single coordinate (say lat|lng|z) starting at index
			internal virtual bool decodeValue(string encoded, AtomicInteger index, AtomicReference coordinate)
			{
				AtomicLong delta = new AtomicLong();
				if (!decodeUnsignedVarint(encoded.ToCharArray(), index, delta))
				{
					return false;
				}
				if ((delta.Get() & 1) != 0)
				{
					delta.Set(~delta.Get());
				}
				delta.Set(delta.Get() >> 1);
				lastValue += delta.Get();
				coordinate.Set(((double)lastValue / multiplier));
				return true;
			}
		}
		/**
		 * 	3rd dimension specification. 
		 *  Example a level, altitude, elevation or some other custom value.
		 *  ABSENT is default when there is no third dimension decoding required.
		 */
		public sealed class ThirdDimension
		{
			public static readonly ThirdDimension ABSENT = new ThirdDimension("ABSENT", InnerEnum.ABSENT, 0);
			public static readonly ThirdDimension LEVEL = new ThirdDimension("LEVEL", InnerEnum.LEVEL, 1);
			public static readonly ThirdDimension ALTITUDE = new ThirdDimension("ALTITUDE", InnerEnum.ALTITUDE, 2);
			public static readonly ThirdDimension ELEVATION = new ThirdDimension("ELEVATION", InnerEnum.ELEVATION, 3);
			public static readonly ThirdDimension RESERVED1 = new ThirdDimension("RESERVED1", InnerEnum.RESERVED1, 4);
			public static readonly ThirdDimension RESERVED2 = new ThirdDimension("RESERVED2", InnerEnum.RESERVED2, 5);
			public static readonly ThirdDimension CUSTOM1 = new ThirdDimension("CUSTOM1", InnerEnum.CUSTOM1, 6);
			public static readonly ThirdDimension CUSTOM2 = new ThirdDimension("CUSTOM2", InnerEnum.CUSTOM2, 7);

			private static readonly List<ThirdDimension> valueList = new List<ThirdDimension>();

			static ThirdDimension()
			{
				valueList.Add(ABSENT);
				valueList.Add(LEVEL);
				valueList.Add(ALTITUDE);
				valueList.Add(ELEVATION);
				valueList.Add(RESERVED1);
				valueList.Add(RESERVED2);
				valueList.Add(CUSTOM1);
				valueList.Add(CUSTOM2);
			}
			public enum InnerEnum
			{
				ABSENT,
				LEVEL,
				ALTITUDE,
				ELEVATION,
				RESERVED1,
				RESERVED2,
				CUSTOM1,
				CUSTOM2
			}

			public readonly InnerEnum innerEnumValue;
			private readonly string nameValue;
			private readonly int ordinalValue;
			private static int nextOrdinal = 0;

			internal int num;

			internal ThirdDimension(string name, InnerEnum innerEnum, int num)
			{
				this.num = num;

				nameValue = name;
				ordinalValue = nextOrdinal++;
				innerEnumValue = innerEnum;
			}
			public int Num
			{
				get
				{
					return num;
				}
			}
			public static ThirdDimension fromNum(long value)
			{
				foreach (ThirdDimension dim in ThirdDimension.values())
				{
					if (dim.Num == value)
					{
						return dim;
					}
				}
				return null;
			}
			public static ThirdDimension[] values()
			{
				return valueList.ToArray();
			}
			public int ordinal()
			{
				return ordinalValue;
			}
			public override string ToString()
			{
				return nameValue;
			}
			public static ThirdDimension valueOf(string name)
			{
				foreach (ThirdDimension enumInstance in ThirdDimension.valueList)
				{
					if (enumInstance.nameValue == name)
					{
						return enumInstance;
					}
				}
				throw new System.ArgumentException(name);
			}
		}
		/**
		 * Coordinate triple
		 */
		public class LatLngZ
		{
			public readonly double lat;
			public readonly double lng;
			public readonly double z;

			public LatLngZ(double latitude, double longitude) : this(latitude, longitude, 0)
			{
			}
			public LatLngZ(double latitude, double longitude, double thirdDimension)
			{
				this.lat = latitude;
				this.lng = longitude;
				this.z = thirdDimension;
			}
			public override string ToString()
			{
				return "LatLngZ [lat=" + lat + ", lng=" + lng + ", z=" + z + "]";
			}
			public override bool Equals(object anObject)
			{
				if (this == anObject)
				{
					return true;
				}
				if (anObject is LatLngZ)
				{
					LatLngZ passed = (LatLngZ)anObject;
					if (passed.lat == this.lat && passed.lng == this.lng && passed.z == this.z)
					{
						return true;
					}
				}
				return false;
			}
		}
	}
}
