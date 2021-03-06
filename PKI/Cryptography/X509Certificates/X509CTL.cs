﻿using PKI;
using PKI.Exceptions;
using PKI.ManagedAPI;
using PKI.Structs;
using PKI.Utils;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using SysadminsLV.Asn1Parser;

namespace System.Security.Cryptography.X509Certificates {
	/// <summary>
	/// Represents a X.509 Certificate Trust List (CTL).
	/// </summary>
	public class X509CTL {
		Wincrypt.CTL_INFO CTLInfo;
		readonly Boolean _isGeneric;
		readonly List<X509Extension> _listExtensions = new List<X509Extension>();

		/// <summary>
		/// Initializes a new instance of the <see cref="X509CTL"/> class. 
		/// </summary>
		public X509CTL() { _isGeneric = false; }
		/// <summary>
		/// Initializes a new instance of the <see cref="X509CTL"/> class using the path to a CTL file. 
		/// </summary>
		/// <param name="path">The path to a CRL file.</param>
		public X509CTL(String path) {
			m_import(Crypt32Managed.CryptFileToBinary(path));
			_isGeneric = true;
		}
		/// <summary>
		/// Initializes a new instance of the <see cref="X509CTL"/> class defined from a sequence of bytes representing
		/// an X.509 certificate trust list.
		/// </summary>
		/// <param name="rawData">A byte array containing data from an X.509 CTL.</param>
		/// <exception cref="ArgumentNullException"></exception>
		public X509CTL(Byte[] rawData) {
			if (rawData == null) { throw new ArgumentNullException(nameof(rawData)); }
			m_import(rawData);
			_isGeneric = true;
		}

		/// <summary>
		/// Gets X.509 certificate trust list (<strong>CTL</strong>) version.
		/// </summary>
		public Int32 Version { get; private set; }
		/// <summary>
		/// Gets a collection of <strong>OIDs</strong> that represents intended usages of the certificate trust list.
		/// </summary>
		public OidCollection SubjectUsage { get; private set; }
		/// <summary>
		/// Gets a string that uniquely identifies the list. This member is used to augment the SubjectUsage and further specifies the list when desired.
		/// </summary>
		public String ListIdentifier { get; private set; }
		/// <summary>
		/// Gets a monotonically increasing number for each update of the <strong>CTL</strong>.
		/// </summary>
		public String SequenceNumber { get; private set; }
		/// <summary>
		/// Gets the issue date of this.
		/// </summary>
		public DateTime ThisUpdate { get; private set; }
		/// <summary>
		/// Indication of the date and time for the CTL's next available scheduled update.
		/// </summary>
		public DateTime NextUpdate { get; private set; }
		/// <summary>
		/// Gets the algorithm type of the <see cref="X509CTLEntry.Thumbprint">Thumbprint</see> in <see cref="X509CTLEntry"/> members of the
		/// <see cref="Entries"/> member array.
		/// </summary>
		public Oid SubjectAlgorithm { get; private set; }
		/// <summary>
		/// Gets a collection of <see cref="X509CTLEntry"/> elements.
		/// </summary>
		public X509CTLEntryCollection Entries { get; private set; }
		/// <summary>
		/// Gets a collection of <see cref="X509Extension">X509Extension</see> objects.
		/// </summary>
		/// <remarks><p>Version 1 CTLs do not support extensions and this property is always empty for them.</p>
		/// </remarks>
		public X509ExtensionCollection Extensions {
			get {
				if (_listExtensions.Count == 0) { return null; }
				X509ExtensionCollection retValue = new X509ExtensionCollection();
				foreach (X509Extension item in _listExtensions) { retValue.Add(item); }
				return retValue;
			}
		}
		/// <summary>
		/// Gets a handle to a Microsoft Cryptographic API CTL context described by an unmanaged
		/// <strong>CTL_CONTEXT</strong> structure.
		/// </summary>
		public IntPtr Handle { get; private set; }
		/// <summary>
		/// Gets the raw data of a certificate trust list.
		/// </summary>
		public Byte[] RawData { get; private set; }

		void get_handle(Byte[] rawData) {
			Handle = Crypt32.CertCreateCTLContext(65537, rawData, (UInt32)rawData.Length);
			if (Handle.Equals(IntPtr.Zero)) {
				throw new Exception("Unable to retieve context. The data is invalid.");
			}
		}
		void get_ctlinfo() {
			Wincrypt.CTL_CONTEXT CTLContext = (Wincrypt.CTL_CONTEXT)Marshal.PtrToStructure(Handle, typeof(Wincrypt.CTL_CONTEXT));
			CTLInfo = (Wincrypt.CTL_INFO)Marshal.PtrToStructure(CTLContext.pCtlInfo, typeof(Wincrypt.CTL_INFO));
			Version = (Int32)CTLInfo.dwVersion + 1;
		}
		void get_usages() {
			SubjectUsage = new OidCollection();
			if (CTLInfo.SubjectUsage.cUsageIdentifier > 0) {
				IntPtr rgpszUseageIdentifier = CTLInfo.SubjectUsage.rgpszUseageIdentifier;
				for (Int32 index = 0; index < CTLInfo.SubjectUsage.cUsageIdentifier; index++) {
					IntPtr pszOid = Marshal.ReadIntPtr(rgpszUseageIdentifier);
					SubjectUsage.Add(new Oid(Marshal.PtrToStringAnsi(pszOid)));
					rgpszUseageIdentifier = (IntPtr)((UInt64)rgpszUseageIdentifier + (UInt32)Marshal.SizeOf(typeof(Wincrypt.CTL_USAGE)));
				}
			}
		}
		void get_identifier() {
			if (CTLInfo.ListIdentifier.cbData != 0) {
				Byte[] rawString = new Byte[CTLInfo.ListIdentifier.cbData];
				Marshal.Copy(CTLInfo.ListIdentifier.pbData,rawString,0,rawString.Length);
				GenericArray.ReverseOrder(ref rawString);
				rawString = Asn1Utils.Encode(rawString, (Byte)Asn1Type.BMPString);
				ListIdentifier = Asn1Utils.DecodeBMPString(rawString);
			}
		}
		void get_serial() {
			StringBuilder SB = new StringBuilder();
			Byte[] seqnumber = new Byte[CTLInfo.SequenceNumber.cbData];
			Marshal.Copy(CTLInfo.SequenceNumber.pbData, seqnumber, 0, seqnumber.Length);
			Array.Reverse(seqnumber);
			foreach (Byte item in seqnumber) { SB.Append($"{item:x2}"); }
			SequenceNumber = SB.ToString();
		}
		void get_ctlentries() {
			if (CTLInfo.cCTLEntry > 0) {
				Entries = new X509CTLEntryCollection();
				IntPtr rgCTLEntry = CTLInfo.rgCTLEntry;
				for (Int32 index = 0; index < CTLInfo.cCTLEntry; index++) {
					StringBuilder SB = new StringBuilder();
					X509AttributeCollection attributes = new X509AttributeCollection();

					Wincrypt.CTL_ENTRY CTLEntry = (Wincrypt.CTL_ENTRY)Marshal.PtrToStructure(rgCTLEntry, typeof(Wincrypt.CTL_ENTRY));
					byte[] bytes = new Byte[CTLEntry.SubjectIdentifier.cbData];
					Marshal.Copy(CTLEntry.SubjectIdentifier.pbData, bytes, 0, bytes.Length);
					foreach (Byte item in bytes) { SB.Append($"{item:X2}"); }
					String thumbprint = SB.ToString();
					if (CTLEntry.cAttribute > 0) {
						IntPtr rgAttribute = CTLEntry.rgAttribute;
						for (Int32 indexx = 0; indexx < CTLEntry.cAttribute; indexx++) {
							Wincrypt.CRYPT_ATTRIBUTE attrib = (Wincrypt.CRYPT_ATTRIBUTE)Marshal.PtrToStructure(rgAttribute, typeof(Wincrypt.CRYPT_ATTRIBUTE));
							Oid pszOid = new Oid(attrib.pszObjId);
							Wincrypt.CRYPTOAPI_BLOB blob = (Wincrypt.CRYPTOAPI_BLOB)Marshal.PtrToStructure(attrib.rgValue, typeof(Wincrypt.CRYPTOAPI_BLOB));
							bytes = new Byte[blob.cbData];
							Marshal.Copy(blob.pbData, bytes, 0, bytes.Length);
							attributes.Add(new X509Attribute(pszOid, bytes));
							rgAttribute = (IntPtr)((UInt64)rgAttribute + (UInt32)Marshal.SizeOf(typeof(Wincrypt.CRYPT_ATTRIBUTE)));
						}
					}
					Entries.Add(new X509CTLEntry(thumbprint, attributes));
					rgCTLEntry = (IntPtr)((UInt64)rgCTLEntry + (UInt32)Marshal.SizeOf(typeof(Wincrypt.CTL_ENTRY)));
				}
			}
		}
		void get_extensions() {
			if (CTLInfo.cExtension > 0) {
				Wincrypt.CERT_EXTENSIONS extstruct = new Wincrypt.CERT_EXTENSIONS {
					rgExtension = CTLInfo.rgExtension,
					cExtension = CTLInfo.cExtension
				};
				_listExtensions.AddRange(CryptographyUtils.DecodeX509ExtensionCollection2(extstruct));
			}
		}
		void get_algorithm() {
			SubjectAlgorithm = new Oid(CTLInfo.SubjectAlgorithm.pszObjId);
		}
		void release_context() {
			Crypt32.CertFreeCTLContext(Handle);
		}
		void m_import(Byte[] rawData) {
			if (!Handle.Equals(IntPtr.Zero)) {
				release_context();
			}
			get_handle(rawData);
			get_ctlinfo();
			get_usages();
			get_identifier();
			get_serial();
			ThisUpdate = DateTime.FromFileTime(CTLInfo.ThisUpdate);
			NextUpdate = DateTime.FromFileTime(CTLInfo.NextUpdate);
			get_ctlentries();
			get_extensions();
			get_algorithm();
			RawData = rawData;
		}

		#region generation functions
		void set_certs(X509Certificate2Collection certs) {
			List<Wincrypt.CTL_ENTRY> entries = (from X509Certificate2 cert in certs where !cert.Handle.Equals(IntPtr.Zero) select create_ctlentry(cert.Handle, cert.Thumbprint)).ToList();
			CTLInfo = new Wincrypt.CTL_INFO {
				cCTLEntry = (UInt32) certs.Count,
				rgCTLEntry = create_ctlentries(entries)
			};
		}
		static IntPtr create_ctlentries(IEnumerable<Wincrypt.CTL_ENTRY> entries) {
			Int32 rgCTLEntrySize = Marshal.SizeOf(typeof(Wincrypt.CTL_ENTRY));
			IntPtr rgCTLEntry = Marshal.AllocHGlobal(rgCTLEntrySize);
			IntPtr ptr = rgCTLEntry;
			foreach (Wincrypt.CTL_ENTRY entry in entries) {
				Marshal.StructureToPtr(entry, ptr, true);
				ptr = (IntPtr)((Int32)ptr + rgCTLEntrySize);
			}
			return rgCTLEntry;
		}
		static Wincrypt.CTL_ENTRY create_ctlentry(IntPtr handle, String thumbprint) {
			Wincrypt.CTL_ENTRY entry = new Wincrypt.CTL_ENTRY {
				SubjectIdentifier = {
					pbData = get_thumbptr(thumbprint),
					cbData = (UInt32)(thumbprint.Length / 2)
				}
			};

			List<UInt32> ids = new List<UInt32>();
			Boolean end = false;
			do {
				UInt32 retn = Crypt32.CertEnumCertificateContextProperties(handle, 0);
				if (retn == 0) { end = true; } else { ids.Add(retn); }
			} while (!end);
			if (ids.Count > 0) {
				entry.cAttribute = (UInt32)ids.Count;
				entry.rgAttribute = create_attributes(handle, ids);
			}
			return entry;
		}
		static IntPtr create_attributes(IntPtr handle, ICollection ids) {
			Int32 rgAttributeSize = Marshal.SizeOf(typeof(Wincrypt.CRYPT_ATTRIBUTE));
			IntPtr rgAttributes = Marshal.AllocHGlobal(Marshal.SizeOf(rgAttributeSize * ids.Count));
			IntPtr ptr = rgAttributes;
			foreach (Wincrypt.CRYPT_ATTRIBUTE attrib in from uint id in ids select create_attribute(handle, id)) {
				Marshal.StructureToPtr(attrib, ptr, true);
				ptr = (IntPtr)((Int32)ptr + rgAttributeSize);
			}
			return rgAttributes;
		}
		static Wincrypt.CRYPT_ATTRIBUTE create_attribute(IntPtr handle, UInt32 propId) {
			UInt32 pcbData = 0;
			Wincrypt.CRYPT_ATTRIBUTE attrib = new Wincrypt.CRYPT_ATTRIBUTE();
			if (Crypt32.CertGetCertificateContextProperty(handle, propId, IntPtr.Zero, ref pcbData)) {
				attrib.rgValue = Marshal.AllocHGlobal((Int32)pcbData);
				attrib.pszObjId = "1.3.6.1.4.1.311.10.11." + propId;
				attrib.cValue = pcbData;
				Crypt32.CertGetCertificateContextProperty(handle, propId, attrib.rgValue, ref pcbData);
			} else {
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			return attrib;
		}
		static IntPtr get_thumbptr(String thumbprint) {
			Byte[] thBytes = AsnFormatter.StringToBinary(thumbprint, EncodingType.HexAny);
			IntPtr thumbPtr = Marshal.AllocHGlobal(thBytes.Length);
			Marshal.Copy(thBytes, 0, thumbPtr, thBytes.Length);
			return thumbPtr;
		}
		#endregion

		void SetSequenceNumber(String number) {
			if (_isGeneric) { throw new InvalidOperationException(); }
			if (String.IsNullOrEmpty(number)) { number = "01"; }
		}
		void SetSubjectUsage(Oid subjectUsage) {
			if (_isGeneric) { throw new InvalidOperationException(); }
			if (subjectUsage == null || String.IsNullOrEmpty(subjectUsage.Value)) {
				
			}
		}
		void SetExtensions(X509ExtensionCollection extensions) {
			if (_isGeneric) { throw new InvalidOperationException(); }
			if (extensions == null) { throw new ArgumentNullException(nameof(extensions)); }
		}
		void ImportCertificates(X509Certificate2Collection certs) {
			if (_isGeneric) { throw new InvalidOperationException(); }
			if (certs == null) { throw new ArgumentNullException(nameof(certs)); }
		}
		void ImportCertificates(X509Certificate2[] certs) {
			if (_isGeneric) { throw new InvalidOperationException(); }
			if (certs == null) { throw new ArgumentNullException(nameof(certs)); }
		}
		/// <summary>
		/// Resets the state of an X509CTL.
		/// </summary>
		/// <remarks>This method can be used to reset the state of the CTL. It also frees any resources associated with the CTL.</remarks>
		public void Reset() {
			if (!Handle.Equals(IntPtr.Zero)) { release_context(); }
			Version = 0;
			SubjectUsage = null;
			SequenceNumber = null;
			ThisUpdate = new DateTime();
			SubjectAlgorithm = null;
			Entries.Reset();
			Entries = null;
			_listExtensions.Clear();
			Handle = IntPtr.Zero;
			RawData = null;
		}
		/// <summary>
		///     Gets a <see cref="SafeCTLHandleContext" /> for the X509 certificate revocation list. The caller of this
		///     method owns the returned safe handle, and should dispose of it when they no longer need it. 
		///     This handle can be used independently of the lifetime of the original X509 certificate revocation list.
		/// </summary>
		/// <returns>Safe handle to a current CTL instance.</returns>
		/// <permission cref="SecurityPermission">
		///     The immediate caller must have SecurityPermission/UnmanagedCode to use this method
		/// </permission>
		public SafeCTLHandleContext GetSafeContext() {
			if (!Handle.Equals(IntPtr.Zero)) {
				SafeCTLHandleContext safeContext = Crypt32.CertDuplicateCTLContext(Handle);
				GC.KeepAlive(this);
				return safeContext;
			}
			throw new UninitializedObjectException();
		}
	}
}
