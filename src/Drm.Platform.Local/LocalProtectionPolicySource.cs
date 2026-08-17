using System.Buffers;
using System.Security;
using System.Text;
using Drm.Application;
using Drm.Policy;

namespace Drm.Platform.Local;

public sealed class LocalFileProtectionPolicySource : IProtectionPolicySource
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async ValueTask<ProtectionPolicySourceReadResult> ReadAsync(
        string location,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(location))
            return new ProtectionPolicySourceReadResult(PolicySourceReadStatus.Unavailable);
        if (Directory.Exists(location))
            return new ProtectionPolicySourceReadResult(PolicySourceReadStatus.Unavailable);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(ProtectionPolicySerializer.MaximumDocumentBytes + 1);
        try
        {
            await using FileStream stream = new(
                location,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            int total = 0;
            while (total <= ProtectionPolicySerializer.MaximumDocumentBytes)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(total, ProtectionPolicySerializer.MaximumDocumentBytes + 1 - total),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
            }

            if (total > ProtectionPolicySerializer.MaximumDocumentBytes)
                return new ProtectionPolicySourceReadResult(PolicySourceReadStatus.TooLarge);

            try
            {
                return ProtectionPolicySourceReadResult.Success(StrictUtf8.GetString(buffer, 0, total));
            }
            catch (DecoderFallbackException)
            {
                return new ProtectionPolicySourceReadResult(PolicySourceReadStatus.InvalidEncoding);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return new ProtectionPolicySourceReadResult(PolicySourceReadStatus.NotFound);
        }
        catch (DirectoryNotFoundException)
        {
            return new ProtectionPolicySourceReadResult(PolicySourceReadStatus.NotFound);
        }
        catch (UnauthorizedAccessException)
        {
            return new ProtectionPolicySourceReadResult(PolicySourceReadStatus.AccessDenied);
        }
        catch (SecurityException)
        {
            return new ProtectionPolicySourceReadResult(PolicySourceReadStatus.AccessDenied);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException
                                          or NotSupportedException or PathTooLongException)
        {
            return new ProtectionPolicySourceReadResult(PolicySourceReadStatus.Unavailable);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
