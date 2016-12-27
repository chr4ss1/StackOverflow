
public async Task Backup()
{
    CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
    CloudBlobClient blobBackupClient = _backupStorageAccount.CreateCloudBlobClient();

    foreach (var srcContainer in blobClient.ListContainers())
    {
        var backupTimeInTicks = DateTime.UtcNow.Ticks;
        var destContainerName = srcContainer.Name + "-" + backupTimeInTicks;

        var destContainer = blobBackupClient.GetContainerReference(destContainerName);

        // assume it does not exist already,
        // as that wouldn't make sense.
        await destContainer.CreateAsync();

        // ensure that the container is not accessible
        // to the outside world,
        // as we want all the backups to be internal.
        BlobContainerPermissions destContainerPermissions = destContainer.GetPermissions();
        if (destContainerPermissions.PublicAccess != BlobContainerPublicAccessType.Off)
        {
            destContainerPermissions.PublicAccess = BlobContainerPublicAccessType.Off;
            await destContainer.SetPermissionsAsync(destContainerPermissions);
        }

        // copy src container to dest container,
        // note that this is synchronous operation in reality,
        // as I want to only add real metadata to container
        // once all the blobs have been copied successfully.
        await CopyContainers(srcContainer, destContainer);
        await EnsureCopySucceeded(destContainer);

        // ensure we have some metadata for the container
        // as this will helps us to delete older containers
        // on a later date.
        await destContainer.FetchAttributesAsync();

        var destContainerMetadata = destContainer.Metadata;
        if (!destContainerMetadata.ContainsKey("Backup-Of"))
        {
            destContainerMetadata.Add("Backup-Of", srcContainer.Name.ToLowerInvariant());
            destContainerMetadata.Add("Created-At", backupTimeInTicks.ToString());
            await destContainer.SetMetadataAsync();
        }
    }

    // let's purge the older containers,
    // if we already have multiple newer backups of them.
    // why keep them around.
    // just asking for trouble.
    var blobGroupedContainers = blobBackupClient.ListContainers()
        .Where(container => container.Metadata.ContainsKey("Backup-Of"))
        .Select(container => new
        {
            Container = container,
            BackupOf = container.Metadata["Backup-Of"],
            CreatedAt = new DateTime(long.Parse(container.Metadata["Created-At"]))
        }).GroupBy(arg => arg.BackupOf);

    foreach (var blobGroupedContainer in blobGroupedContainers)
    {
        var containersToDelete = blobGroupedContainer.Select(arg => new
        {
            Container = arg.Container,
            CreatedAt = new DateTime(arg.CreatedAt.Year, arg.CreatedAt.Month, arg.CreatedAt.Day)
        })
            .GroupBy(arg => arg.CreatedAt)
            .OrderByDescending(grouping => grouping.Key)
            .Skip(7) /* skip last 7 days worth of data */
            .SelectMany(grouping => grouping)
            .Select(arg => arg.Container);

        foreach (var containerToDelete in containersToDelete)
        {
            await containerToDelete.DeleteIfExistsAsync();
        }
    }
}

private async Task EnsureCopySucceeded(CloudBlobContainer destContainer)
{
    bool pendingCopy = true;
    var retryCountLookup = new Dictionary<string, int>();

    while (pendingCopy)
    {
        pendingCopy = false;

        var destBlobList = destContainer.ListBlobs(null, true, BlobListingDetails.Copy);

        foreach (var dest in destBlobList)
        {
            var destBlob = dest as CloudBlob;
            if (destBlob == null)
            {
                continue;
            }

            var blobIdentifier = destBlob.Name;

            if (destBlob.CopyState.Status == CopyStatus.Aborted ||
                destBlob.CopyState.Status == CopyStatus.Failed)
            {
                int retryCount;
                if (retryCountLookup.TryGetValue(blobIdentifier, out retryCount))
                {
                    if (retryCount > 4)
                    {
                        throw new Exception("[CRITICAL] Failed to copy '"
                                                + destBlob.CopyState.Source.AbsolutePath + "' to '"
                                                + destBlob.StorageUri + "' due to reason of: " +
                                                destBlob.CopyState.StatusDescription);
                    }

                    retryCountLookup[blobIdentifier] = retryCount + 1;
                }
                else
                {
                    retryCountLookup[blobIdentifier] = 1;
                }

                pendingCopy = true;

                // restart the copy process for src and dest blobs.
                // note we also have retry count protection,
                // so if any of the blobs fail too much,
                // we'll give up.
                await destBlob.StartCopyAsync(destBlob.CopyState.Source);
            }
            else if (destBlob.CopyState.Status == CopyStatus.Pending)
            {
                pendingCopy = true;
            }
        }

        Thread.Sleep(1000);
    }
}

private async Task CopyContainers(
        CloudBlobContainer srcContainer,
        CloudBlobContainer destContainer)
{
    // get the SAS token to use for all blobs
    string blobToken = srcContainer.GetSharedAccessSignature(new SharedAccessBlobPolicy()
    {
        Permissions = SharedAccessBlobPermissions.Read,
        SharedAccessStartTime = DateTime.Now.AddMinutes(-5),
        SharedAccessExpiryTime = DateTime.Now.AddHours(3)
    });

    foreach (var srcBlob in srcContainer.ListBlobs(null, true))
    {
        var srcCloudBlob = srcBlob as CloudBlob;
        if (srcCloudBlob == null)
        {
            continue;
        }

        CloudBlob destCloudBlob;

        if (srcCloudBlob.Properties.BlobType == BlobType.BlockBlob)
        {
            destCloudBlob = destContainer.GetBlockBlobReference(srcCloudBlob.Name);
        }
        else
        {
            destCloudBlob = destContainer.GetPageBlobReference(srcCloudBlob.Name);
        }

        await destCloudBlob.StartCopyAsync(new Uri(srcCloudBlob.Uri.AbsoluteUri + blobToken));
    }
}
