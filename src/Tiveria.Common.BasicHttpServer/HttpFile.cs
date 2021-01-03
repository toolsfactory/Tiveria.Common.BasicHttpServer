using System;
using System.IO;

namespace Tiveria.Common.Net
{
    /// <summary>
    /// HTTP file data container.
    /// </summary>
    public class HttpFile : IDisposable
    {
        /// <summary>
        /// Creates new HTTP file data container.
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <param name="value">Data.</param>
        /// <param name="contentType">Content type.</param>
        internal HttpFile(string fileName, Stream value, string contentType)
        {
            Value = value;
            FileName = fileName;
            ContentType = contentType;
        }

        /// <summary>
        /// Gets the name of the file.
        /// </summary>
        public string FileName { get; private set; }

        /// <summary>
        /// Gets the data.
        /// <para>If a stream is created <see cref="OnFile"/> it will be closed when this HttpFile object is disposed.</para>
        /// </summary>
        public Stream Value { get; private set; }

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; private set; }

        /// <summary>
        /// Saves the data into a file.
        /// <para>Directory path will be auto created if does not exists.</para>
        /// </summary>
        /// <param name="fileName">File path with name.</param>
        /// <param name="overwrite">True to overwrite the existing file, false otherwise.</param>
        /// <returns>True if the file is saved/overwritten, false otherwise.</returns>
        public bool Save(string fileName, bool overwrite = false)
        {
            if (File.Exists(Path.GetFullPath(fileName)))
                return false;

            var dir = Path.GetDirectoryName(Path.GetFullPath(fileName));
            Directory.CreateDirectory(dir);

            Value.Position = 0;
            using (var outStream = File.OpenWrite(fileName))
                Value.CopyTo(outStream);

            return true;
        }

        /// <summary>
        /// Disposes the current instance.
        /// </summary>
        public void Dispose()
        {
            if (Value != null)
            {
                Value?.Dispose();
                Value = null;
            }
        }

        /// <summary>
        /// Disposes the current instance.
        /// </summary>
        ~HttpFile()
        {
            Dispose();
        }
    }
}