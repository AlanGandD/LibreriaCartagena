using System.Net;
using System.Net.Mail;

namespace Cartagena.Api.Services;

public sealed class MailService(IConfiguration settings, ILogger<MailService> logger)
{
    public async Task<bool> SendAsync(string destination,string subject,string message,byte[]? receipt=null)
    {
        var host=settings["SMTP_HOST"];
        var user=settings["SMTP_USER"];
        var pass=settings["SMTP_PASSWORD"];
        var from=settings["SMTP_FROM"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(from)) return false;
        try
        {
            using var client=new SmtpClient(host,int.Parse(settings["SMTP_PORT"]??"587"))
            {
                EnableSsl=true,Credentials=new NetworkCredential(user,pass),Timeout=12000
            };
            using var mail=new MailMessage(from,destination,subject,message);
            if (receipt is not null)
            {
                var mem=new MemoryStream(receipt);
                mail.Attachments.Add(new Attachment(mem,"ticket.pdf","application/pdf"));
            }
            await client.SendMailAsync(mail);
            return true;
        }
        catch(Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex,"No se pudo enviar el correo");
            return false;
        }
    }
}
