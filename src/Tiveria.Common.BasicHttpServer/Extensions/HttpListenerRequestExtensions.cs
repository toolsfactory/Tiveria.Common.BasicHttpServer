using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Tiveria.Common.Net;

namespace System.Net
{
    /// <summary>
    /// Class containing <see cref="HttpListenerRequest"/> extensions.
    /// </summary>
    public static partial class HttpListenerRequestExtensions
    {
        /// <summary>
        /// Delegate executed when a file is about to be read from a body stream.
        /// </summary>
        /// <param name="fieldName">Field name.</param>
        /// <param name="fileName">name of the file.</param>
        /// <param name="contentType">Content type.</param>
        /// <returns>Stream to be populated.</returns>
        public delegate Stream OnFile(string fieldName, string fileName, string contentType);


            static Dictionary<string, HttpFile> ParseMultipartForm(HttpListenerRequest request, Dictionary<string, string> args, OnFile onFile)
            {
                if (request.ContentType.StartsWith("multipart/form-data") == false)
                    throw new InvalidDataException("Not 'multipart/form-data'.");

                var boundary = Regex.Match(request.ContentType, "boundary=(.+)").Groups[1].Value;
                boundary = "--" + boundary;


                var files = new Dictionary<string, HttpFile>();
                var inputStream = new BufferedStream(request.InputStream);

                parseUntillBoundaryEnd(inputStream, new MemoryStream(), boundary);
                while (true)
                {
                    var (n, v, fn, ct) = parseSection(inputStream, "\r\n" + boundary, onFile);
                    if (String.IsNullOrEmpty(n)) break;

                    v.Position = 0;
                    if (!String.IsNullOrEmpty(fn))
                        files.Add(n, new HttpFile(fn, v, ct));
                    else
                        args.Add(n, readAsString(v));
                }

                return files;
            }

            private static (string Name, Stream Value, string FileName, string ContentType) parseSection(Stream source, string boundary, OnFile onFile)
            {
                var (n, fn, ct) = readContentDisposition(source);
                source.ReadByte(); source.ReadByte(); //\r\n (empty row)

                var dst = String.IsNullOrEmpty(fn) ? new MemoryStream() : onFile(n, fn, ct);
                if (dst == null)
                    throw new ArgumentException(nameof(onFile), "The on-file callback must return a stream.");

                parseUntillBoundaryEnd(source, dst, boundary);

                return (n, dst, fn, ct);
            }

            private static (string Name, string FileName, string ContentType) readContentDisposition(Stream stream)
            {
                const string UTF_FNAME = "utf-8''";

                var l = readLine(stream);
                if (String.IsNullOrEmpty(l))
                    return (null, null, null);

                //(regex matches are taken from NancyFX) and modified
                var n = Regex.Match(l, @"name=""?(?<n>[^\""]*)").Groups["n"].Value;
                var f = Regex.Match(l, @"filename\*?=""?(?<f>[^\"";]*)").Groups["f"]?.Value;

                string cType = null;
                if (!String.IsNullOrEmpty(f))
                {
                    if (f.StartsWith(UTF_FNAME))
                        f = Uri.UnescapeDataString(f.Substring(UTF_FNAME.Length));

                    l = readLine(stream);
                    cType = Regex.Match(l, "Content-Type: (?<cType>.+)").Groups["cType"].Value;
                }

                return (n, f, cType);
            }

            private static void parseUntillBoundaryEnd(Stream source, Stream destination, string boundary)
            {
                var checkBuffer = new byte[boundary.Length]; //for boundary checking

                int b, i = 0;
                while ((b = source.ReadByte()) != -1)
                {
                    if (i == boundary.Length) //boundary found -> go to the end of line
                    {
                        if (b == '\n') break;
                        continue;
                    }

                    if (b == boundary[i]) //start filling the check buffer
                    {
                        checkBuffer[i] = (byte)b;
                        i++;
                    }
                    else
                    {
                        var idx = 0;
                        while (idx < i) //write the buffer data to stream
                        {
                            destination.WriteByte(checkBuffer[idx]);
                            idx++;
                        }

                        i = 0;
                        destination.WriteByte((byte)b); //write the current byte
                    }
                }
            }

            private static string readLine(Stream stream)
            {
                var sb = new StringBuilder();

                int b;
                while ((b = stream.ReadByte()) != -1 && b != '\n')
                    sb.Append((char)b);

                if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                    sb.Remove(sb.Length - 1, 1);

                return sb.ToString();
            }

            private static string readAsString(Stream stream)
            {
                var sb = new StringBuilder();

                int b;
                while ((b = stream.ReadByte()) != -1)
                    sb.Append((char)b);

                return sb.ToString();
            }
        
        static bool ParseForm(this HttpListenerRequest request, Dictionary<string, string> args)
        {
            if (request.ContentType != "application/x-www-form-urlencoded")
                return false;

            var str = request.BodyAsString();
            if (str == null)
                return false;

            foreach (var pair in str.Split('&'))
            {
                var nameValue = pair.Split('=');
                if (nameValue.Length != (1 + 1))
                    continue;

                args.Add(nameValue[0], WebUtility.UrlDecode(nameValue[1]));
            }

            return true;
        }

        static string BodyAsString(this HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
                return null;

            string str = null;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                str = reader.ReadToEnd();
            }

            return str;
        }
        /// <summary>
        /// Parses body of the request including form and multi-part form data.
        /// </summary>
        /// <param name="request">HTTP request.</param>
        /// <param name="args">Key-value pairs populated by the form data by this function.</param>
        /// <returns>Name-file pair collection.</returns>
        public static Dictionary<string, HttpFile> ParseBody(this HttpListenerRequest request, Dictionary<string, string> args)
        {
            return request.ParseBody(args, (n, fn, ct) => new MemoryStream());
        }

        /// <summary>
        /// Parses body of the request including form and multi-part form data.
        /// </summary>
        /// <param name="request">HTTP request.</param>
        /// <param name="args">Key-value pairs populated by the form data by this function.</param>
        /// <param name="onFile">
        /// Function called if a file is about to be parsed. The stream is attached to a corresponding <see cref="HttpFile"/>.
        /// <para>By default, <see cref="MemoryStream"/> is used, but for large files, it is recommended to open <see cref="FileStream"/> directly.</para>
        /// </param>
        /// <returns>Name-file pair collection.</returns>
        public static Dictionary<string, HttpFile> ParseBody(this HttpListenerRequest request, Dictionary<string, string> args, OnFile onFile)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (args == null)
                throw new ArgumentNullException(nameof(args));

            if (onFile == null)
                throw new ArgumentNullException(nameof(onFile));


            var files = new Dictionary<string, HttpFile>();

            if (request.ContentType.StartsWith("application/x-www-form-urlencoded"))
            {
                ParseForm(request, args);
            }
            else if (request.ContentType.StartsWith("multipart/form-data"))
            {
                files = ParseMultipartForm(request, args, onFile);
            }
            else
                throw new NotSupportedException("The body content-type is not supported.");

            return files;
        }
    }
}