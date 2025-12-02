# Troubleshooting Cloud Database Connection 🔧

## Current Issue
Cloud Database connection is **failing**. Here's how to fix it:

## Step 1: Check Remote Access Settings

Since you're connecting from your **local machine** (not a hosted website), you need to use **"Remote access"** settings from MonsterAPI, not "Local access".

1. Go to your MonsterAPI dashboard
2. Click on the **"Remote access"** tab (not "Local access")
3. Copy the connection string or connection details from there

## Step 2: Common Connection String Formats

Try these different formats in `App.config`:

### Format 1: Standard with Encryption (Current)
```xml
<add name="CloudConnection"
     connectionString="Data Source=db33496.databaseasp.net,1433;Initial Catalog=db33496;User ID=db33496;Password=4r%25M_6Wi3f%23P;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connection Timeout=30;" />
```

### Format 2: Without Encryption (If Format 1 fails)
```xml
<add name="CloudConnection"
     connectionString="Data Source=db33496.databaseasp.net,1433;Initial Catalog=db33496;User ID=db33496;Password=4r%25M_6Wi3f%23P;Encrypt=False;MultipleActiveResultSets=True;Connection Timeout=30;" />
```

### Format 3: Using Server= format
```xml
<add name="CloudConnection"
     connectionString="Server=db33496.databaseasp.net,1433;Database=db33496;User Id=db33496;Password=4r%25M_6Wi3f%23P;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connection Timeout=30;" />
```

## Step 3: Password Encoding

If your password contains special characters, they need to be URL-encoded:
- `%` becomes `%25`
- `#` becomes `%23`
- `&` becomes `%26`
- `@` becomes `%40`

Your password `4r%M_6Wi3f#P` should be encoded as `4r%25M_6Wi3f%23P`

## Step 4: Common Issues & Solutions

### Issue 1: Firewall Blocking Port 1433
**Solution:**
- Check if your firewall allows outbound connections on port 1433
- Some networks block SQL Server ports
- Try from a different network (mobile hotspot) to test

### Issue 2: Server Not Allowing Remote Connections
**Solution:**
- Check MonsterAPI dashboard for "Remote access" settings
- Some free plans may restrict remote access
- Contact MonsterAPI support if remote access is disabled

### Issue 3: Wrong Server Address
**Solution:**
- Use the exact server name from MonsterAPI (not IP address)
- Make sure you're using the "Remote access" server name, not "Local access"

### Issue 4: Timeout Errors
**Solution:**
- Increase `Connection Timeout=60` or higher
- Check your internet connection
- Try again during off-peak hours

### Issue 5: Authentication Failed
**Solution:**
- Double-check username and password
- Make sure password is properly URL-encoded
- Verify credentials in MonsterAPI dashboard

## Step 5: Test the Connection

1. Update `App.config` with one of the connection string formats above
2. Restart your application
3. Go to Database Sync page
4. Click "Test Connections"
5. Check the error message (it will now show detailed error)

## Step 6: Check Error Messages

The improved error display will show you:
- **Network-related errors**: "A network-related or instance-specific error"
  - → Check firewall, network, server address
  
- **Authentication errors**: "Login failed for user"
  - → Check username/password
  
- **Timeout errors**: "Timeout expired"
  - → Increase Connection Timeout value
  
- **Server not found**: "Cannot open server"
  - → Check server name/address

## Alternative: Use Remote Access IP/Port

If MonsterAPI provides a different IP address or port for remote access:
1. Get the remote access details from MonsterAPI dashboard
2. Update the connection string with those values
3. Some providers use different ports (not 1433) for remote access

## Quick Test

Try this connection string format (most compatible):
```xml
<add name="CloudConnection"
     connectionString="Data Source=db33496.databaseasp.net,1433;Initial Catalog=db33496;User ID=db33496;Password=4r%25M_6Wi3f%23P;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connection Timeout=60;Persist Security Info=False;" />
```

## Still Not Working?

1. **Check MonsterAPI Dashboard:**
   - Is "Remote access" enabled?
   - What connection string does it show?
   - Are there any IP whitelist restrictions?

2. **Test with SQL Server Management Studio (SSMS):**
   - Try connecting directly with SSMS
   - If SSMS works, copy that exact connection string format
   - If SSMS fails, the issue is with MonsterAPI settings

3. **Contact MonsterAPI Support:**
   - Ask for remote connection details
   - Verify your plan allows remote connections
   - Get the correct connection string format

## Next Steps

After fixing the connection:
1. Test connections again
2. Once both show "Connected", you can sync
3. The sync will copy all your local data to cloud

