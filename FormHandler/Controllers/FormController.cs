using Microsoft.AspNetCore.Mvc;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FormHandler.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FormController(ILogger<FormController> logger, IConfiguration configuration) : ControllerBase
    {
        IConfiguration Settings => configuration.GetSection("FormHandler");

        public async Task Post()
        {
            var recaptchaSecret = Settings["GoogleReCaptchaSecretKey"];
            bool isValid = true;
            FormCollection form = (FormCollection)Request.Form;
            if (recaptchaSecret is not null && form["g-recaptcha-verify"].Count > 0)
            {
                Dictionary<string, string> Values = new Dictionary<string, string>
                {
                    { "secret", Settings["GoogleReCaptchaSecretKey"]! },
                    { "response", form["g-recaptcha-verify"].ToString().Trim(',') },
                    { "remoteip", Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "" }
                };
                Dictionary<string, object> resp;
                using (HttpClient client = new HttpClient())
                {
                    using (var postContent = new FormUrlEncodedContent(Values))
                    using (HttpResponseMessage response = client.PostAsync("https://www.google.com/recaptcha/api/siteverify", postContent).Result)
                    {
                        response.EnsureSuccessStatusCode();
                        using (HttpContent content = response.Content)
                        {
                            string result = content.ReadAsStringAsync().Result;
                            resp = JsonSerializer.Deserialize<Dictionary<string, object>>(result)!;
                        }
                    }
                    if (resp.TryGetValue("success", out object? value))
                        isValid = (bool)value;
                }
            }

            if (isValid)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<h2>New Form Submission</h2>");
                foreach (var key in form.Keys)
                {
                    if (key == "g-recaptcha-verify") continue;
                    sb.AppendLine($"<b>{System.Net.WebUtility.HtmlEncode(key)}</b>: {System.Net.WebUtility.HtmlEncode(form[key])}<br/>");
                }
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress(Settings["SenderEmail"], Settings["SenderEmail"]));
                msg.To.Add(new MailboxAddress(Settings["RecipientEmail"], Settings["RecipientEmail"]));
                msg.Subject = "";

                msg.Body = new BodyBuilder
                {
                    HtmlBody = sb.ToString()
                }
                .ToMessageBody();
                using (var client = new MailKit.Net.Smtp.SmtpClient())
                {
                    await client.ConnectAsync(Settings["SmtpHost"], int.Parse(Settings["SmtpPort"] ?? "587"), MailKit.Security.SecureSocketOptions.StartTls);
                    if (!string.IsNullOrEmpty(Settings["SmtpUsername"]))
                    {
                        await client.AuthenticateAsync(Settings["SmtpUsername"], Settings["SmtpPassword"]);
                    }
                    await client.SendAsync(msg);
                    await client.DisconnectAsync(true);
                }
            }

        }
    }
}
