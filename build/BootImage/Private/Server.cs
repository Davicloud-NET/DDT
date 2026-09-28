// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// DDT's API for the boot image build, trusting the pinned root and no other. C# 5 and the .NET Framework's X509Chain,
// so Windows PowerShell 5.1 compiles and runs it too.
public static class DdtBootImageServer
{
    public static HttpClient Connect(byte[] rootCertificate, string token)
    {
        X509Certificate2 root = new X509Certificate2(rootCertificate);
        HttpClientHandler handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback =
            delegate (HttpRequestMessage request, X509Certificate2 certificate, X509Chain presented, SslPolicyErrors errors)
            {
                return IsPinned(root, certificate, presented, errors);
            };

        HttpClient client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromMinutes(30);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    public static string GetString(HttpClient client, string url)
    {
        using (HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult())
        {
            EnsureSuccess(response, url);

            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
    }

    public static void Download(HttpClient client, string url, string path)
    {
        using (HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
        {
            EnsureSuccess(response, url);

            using (Stream source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
            using (FileStream target = File.Create(path))
            {
                source.CopyTo(target);
            }
        }
    }

    // The chain the server presented only lends its intermediates. The chain is built again with the pinned root as
    // the one root there is, and its top must be that root.
    private static bool IsPinned(X509Certificate2 root, X509Certificate2 certificate, X509Chain presented, SslPolicyErrors errors)
    {
        SslPolicyErrors refused = SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable;
        if (certificate == null || (errors & refused) != 0)
        {
            return false;
        }

        using (X509Chain chain = new X509Chain())
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.Add(root);

            if (presented != null)
            {
                foreach (X509ChainElement element in presented.ChainElements)
                {
                    chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }

            if (!chain.Build(certificate))
            {
                return false;
            }

            X509Certificate2 top = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;

            return Convert.ToBase64String(top.RawData) == Convert.ToBase64String(root.RawData);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int status = (int)response.StatusCode;
        string reason = status == 401
            ? "DDT refused the API token: it is unknown, revoked or expired, or its user is disabled or locked out."
            : status == 403
                ? "The API token may not download boot image drivers. That takes an administrator's token."
                : "DDT answered " + status + " " + response.ReasonPhrase + ".";

        throw new InvalidOperationException(url + ": " + reason);
    }
}
